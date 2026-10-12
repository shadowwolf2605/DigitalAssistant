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

// Базата данни: PostgreSQL, ако е зададена връзка (в интернет, напр. Neon), иначе файлът SQLite в App_Data
var postgres = PostgresSettings.ConnectionString(builder.Configuration);
var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Default"));
DatabaseKeyStore? databaseKeys = null;
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("DigitalAssistant");
if (postgres != null)
{
    // Датите се пазят като местно време, както в SQLite
    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(postgres));
    // Няма постоянен диск, затова ключовете за шифроване се пазят в базата (шифровани с Keys:Secret)
    databaseKeys = new DatabaseKeyStore(postgres, builder.Configuration["Keys:Secret"]);
    builder.Services.AddSingleton<IKeyStore>(databaseKeys);
    dataProtection.AddKeyManagementOptions(options => options.XmlRepository = databaseKeys);
}
else
{
    // Файлът на базата е в папката на проекта, откъдето и да се стартира приложението
    if (!Path.IsPathRooted(connection.DataSource))
        connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
    Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)!);
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection.ToString()));

    // Ключовете за шифроване (бисквитки за вход, ЕГН) се пазят в App_Data/Keys
    builder.Services.AddSingleton<IKeyStore>(new FileKeyStore(Path.Combine(appData, "Keys")));
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(appData, "Keys")));
    // На Windows ключът, с който се шифрова ЕГН, се пази защитен с профила на Windows
    if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
}

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
builder.Services.AddSingleton<ScheduledReplySender>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScheduledReplySender>());

var app = builder.Build();

// Създава базата данни и примерните данни при първо стартиране.
// Ако базата е от по-стара версия на системата, тя се премества в App_Data/Backups и се създава нова.
if (databaseKeys == null)
    DatabaseVersion.Check(connection.DataSource, Path.Combine(appData, "Backups"), app.Logger);
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var created = db.Database.EnsureCreated();
    if (databaseKeys != null)
    {
        databaseKeys.EnsureTable();
        if (!databaseKeys.HasSecret)
            app.Logger.LogWarning("Липсва Keys:Secret – ключовете за шифроване се пазят в базата без допълнителна защита.");
        if (created) databaseKeys.SetVersion(AppDbContext.SchemaVersion);
        else if (databaseKeys.GetVersion() is long version && version != AppDbContext.SchemaVersion)
        {
            // Базата в интернет се допълва с новите таблици и колони, без да се губят данни
            if (version < AppDbContext.SchemaVersion &&
                Enumerable.Range((int)version, AppDbContext.SchemaVersion - (int)version).All(v => DatabaseVersion.PostgresUpgrades.ContainsKey(v)))
            {
                using var transaction = db.Database.BeginTransaction();
                for (var v = version; v < AppDbContext.SchemaVersion; v++)
                    db.Database.ExecuteSqlRaw(DatabaseVersion.PostgresUpgrades[v]);
                transaction.Commit();
                databaseKeys.SetVersion(AppDbContext.SchemaVersion);
                app.Logger.LogInformation("Базата в PostgreSQL е обновена от версия {Old} до версия {New}. Данните са запазени.", version, AppDbContext.SchemaVersion);
            }
            else
                app.Logger.LogError("Базата в PostgreSQL е от версия {Old}, а системата е версия {New}. Тази промяна не може да се направи автоматично.", version, AppDbContext.SchemaVersion);
        }
    }
    else if (created)
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

// Кратка проверка дали сайтът работи (за Render и UptimeRobot). Не отваря базата, за да може тя да „спи“.
app.MapGet("/health", () => Results.Text("OK"));

app.MapControllerRoute(
    name: "admin",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
