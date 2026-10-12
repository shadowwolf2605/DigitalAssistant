using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using DigitalAssistant.Data;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

// Числата се въвеждат с точка (2.50), датите се показват като дд.мм.гггг
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
QuestPDF.Settings.License = LicenseType.Community;
// Съобщенията в черния прозорец да се виждат на кирилица, а не като ???
try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { } // няма прозорец (например на сървър)

var builder = WebApplication.CreateBuilder(args);
var appData = Path.Combine(builder.Environment.ContentRootPath, "App_Data");

// Съобщенията и грешките се записват и във файл (App_Data/Logs)
builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(appData, "Logs")));

// Настройки от appsettings.json
var adminSite = builder.Configuration.GetSection("AdminSite").Get<AdminSiteOptions>() ?? new AdminSiteOptions();
builder.Services.AddSingleton(adminSite);
builder.Services.AddSingleton(builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Ai").Get<AiOptions>() ?? new AiOptions());

builder.Services.AddControllersWithViews(options =>
{
    // Съобщения за грешки на български
    var m = options.ModelBindingMessageProvider;
    m.SetValueMustBeANumberAccessor(f => $"Полето „{f}“ трябва да е число.");
    m.SetAttemptedValueIsInvalidAccessor((v, f) => $"Стойността „{v}“ не е валидна за полето „{f}“.");
    m.SetValueIsInvalidAccessor(v => $"Стойността „{v}“ не е валидна.");
    // Тук се подава стойността (празна), а не името на полето
    m.SetValueMustNotBeNullAccessor(_ => "Попълнете всички задължителни полета.");
    m.SetMissingBindRequiredValueAccessor(f => $"Полето „{f}“ е задължително.");
    m.SetMissingKeyOrValueAccessor(() => "Полето е задължително.");
    m.SetNonPropertyAttemptedValueIsInvalidAccessor(v => $"Стойността „{v}“ не е валидна.");
    m.SetNonPropertyValueMustBeANumberAccessor(() => "Полето трябва да е число.");
    m.SetUnknownValueIsInvalidAccessor(f => $"Стойността за „{f}“ не е валидна.");
});

// Кирилицата се записва в HTML като нормален текст, а не като кодове
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

// Файлът на базата е в папката на проекта, откъдето и да се стартира приложението
var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Default"));
if (!Path.IsPathRooted(connection.DataSource))
    connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)!);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection.ToString()));

// Ключовете за шифроване (бисквитки за вход, ЕГН) се пазят в App_Data/Keys
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(appData, "Keys")))
    .SetApplicationName("DigitalAssistant");
// На Windows ключът, с който се шифрова ЕГН, се пази защитен с профила на Windows
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();

// Страниците под /Admin ползват входа на администраторите, а сайтът за гражданите – техния вход.
// Така на всяка страница „текущият потребител“ е правилният и формулярите (напр. „Изход“) работят навсякъде.
builder.Services.AddAuthentication(CitizenAuth.AutoScheme)
    .AddPolicyScheme(CitizenAuth.AutoScheme, "Администратор или гражданин", options =>
        options.ForwardDefaultSelector = context => context.Request.Path.StartsWithSegments("/Admin")
            ? CookieAuthenticationDefaults.AuthenticationScheme
            : CitizenAuth.Scheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Admin/Account/Login";
        options.AccessDeniedPath = "/Admin/Account/Login";
        // След 30 минути без работа администраторът излиза автоматично
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.Cookie.Name = "DigitalAssistant.Admin";
        // Бисквитката се изпраща само към страниците на администрацията
        options.Cookie.Path = "/Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Events.OnValidatePrincipal = AdminSession.ValidateAsync;
        options.Events.OnRedirectToLogin = LoginRedirect.For(options.Events.OnRedirectToLogin);
        options.CookieManager = new SchemeCookieManager();
    })
    // Отделен вход за гражданите, за да не се смесва с входа на администратора
    .AddCookie(CitizenAuth.Scheme, options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;
        options.Cookie.Name = "DigitalAssistant.Citizen";
        options.CookieManager = new SchemeCookieManager();
        options.Events.OnValidatePrincipal = CitizenAuth.ValidateAsync;
        options.Events.OnRedirectToLogin = LoginRedirect.For(options.Events.OnRedirectToLogin);
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

// Ограничение на броя заявки от един компютър (защита от претоварване и от отгатване на пароли)
// Броят се задава в раздел "Limits" на appsettings.json.
var questionsPerMinute = builder.Configuration.GetValue("Limits:QuestionsPerMinute", 20);
var loginsPer5Minutes = builder.Configuration.GetValue("Limits:LoginsPer5Minutes", 30);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    options.AddPolicy("assistant", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = questionsPerMinute, Window = TimeSpan.FromMinutes(1) }));
    // Стъпките на помощника – с по-голям лимит, защото човек натиска бързо
    options.AddPolicy("guide", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = questionsPerMinute * 6, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginsPer5Minutes, Window = TimeSpan.FromMinutes(5) }));
    options.OnRejected = async (context, token) =>
    {
        if (context.HttpContext.Request.Path.StartsWithSegments("/Assistant"))
        {
            const string text = "Търсихте много пъти за кратко време. Изчакайте една минута и опитайте отново.";
            context.HttpContext.Response.ContentType = "text/html; charset=utf-8";
            var html = context.HttpContext.Request.Headers.ContainsKey("X-Guide")
                ? $"<div class=\"guide-card\"><div class=\"alert alert-warning mb-3\" role=\"alert\">{text}</div><a class=\"btn btn-light-glass\" href=\"/Assistant\" data-guide>Изберете тема</a></div>"
                : $"<!DOCTYPE html><html lang=\"bg\"><meta charset=\"utf-8\"><title>Изчакайте</title><p>{text}</p><p><a href=\"/Assistant\">Към помощника</a></p></html>";
            await context.HttpContext.Response.WriteAsync(html, token);
        }
    };
});

builder.Services.AddMemoryCache();
builder.Services.AddScoped<AssistantService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddSingleton<ExportService>();
builder.Services.AddSingleton<AiAssistant>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<PersonalDataProtector>();
builder.Services.AddSingleton<SiteUrls>();
builder.Services.AddScoped<Notifications>();
builder.Services.AddHostedService<BackupService>();
builder.Services.AddHostedService<ScheduledReplySender>();

var app = builder.Build();

// Създава базата данни и примерните данни при първо стартиране.
// Ако базата е от по-стара версия на системата, тя се премества в App_Data/Backups и се създава нова.
DatabaseVersion.Check(connection.DataSource, Path.Combine(appData, "Backups"), app.Logger);
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.EnsureCreated())
        DatabaseVersion.Set(db);
    await SeedData.InitializeAsync(db, app.Environment.ContentRootPath, scope.ServiceProvider.GetRequiredService<PersonalDataProtector>(), adminSite.InitialPassword);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}");
app.UseMiddleware<HttpsRedirectMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles();
// Администрацията е на отделен адрес (порт) и само от вътрешната мрежа
app.UseMiddleware<AdminSiteMiddleware>();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "admin",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
