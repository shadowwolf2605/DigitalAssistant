using System.Security.Cryptography;
using System.Text;
using DigitalAssistant.Data;
using DigitalAssistant.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Services;

// Писмата до гражданите: потвърждение на имейла, нова парола, подадено заявление и промени по заявлението
public class Notifications
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly SiteUrls _urls;

    public Notifications(AppDbContext db, EmailService email, SiteUrls urls)
    {
        _db = db;
        _email = email;
        _urls = urls;
    }

    public async Task SendEmailConfirmationAsync(Citizen citizen)
    {
        var token = await CreateTokenAsync(citizen, CitizenToken.ConfirmEmail, TimeSpan.FromHours(48));
        await _email.SendAsync(citizen.Email, "Потвърдете имейла си",
            new[]
            {
                $"Здравейте, {citizen.FullName}!",
                "Благодарим ви за регистрацията в „Дигитален помощник за административни услуги“.",
                "За да можете да подавате заявления онлайн, потвърдете имейла си с бутона по-долу. Връзката важи 48 часа."
            },
            "Потвърди имейла", _urls.Public($"/Account/ConfirmEmail?token={token}"));
    }

    public async Task SendPasswordResetAsync(Citizen citizen)
    {
        var token = await CreateTokenAsync(citizen, CitizenToken.ResetPassword, TimeSpan.FromHours(1));
        await _email.SendAsync(citizen.Email, "Нова парола",
            new[]
            {
                $"Здравейте, {citizen.FullName}!",
                "Получихме искане за нова парола за вашия профил. Натиснете бутона по-долу и въведете новата парола. Връзката важи 1 час и може да се използва само веднъж.",
                "Ако не сте искали нова парола, не правете нищо. Старата ви парола продължава да работи."
            },
            "Задай нова парола", _urls.Public($"/Account/ResetPassword?token={token}"));
    }

    public Task ApplicationSubmittedAsync(Application app, Citizen citizen, string serviceName)
    {
        if (!citizen.EmailNotifications) return Task.CompletedTask;
        return _email.SendAsync(citizen.Email, $"Заявление № {app.Number} е подадено",
            new[]
            {
                $"Здравейте, {citizen.FullName}!",
                $"Получихме вашето заявление за услуга „{serviceName}“. Номерът му е № {app.Number}.",
                "Ще ви пишем, когато служителят промени статуса или ви изпрати съобщение."
            },
            "Виж заявлението", _urls.Public($"/Applications/Details/{app.Id}"));
    }

    public Task StaffUpdateAsync(Application app, Citizen citizen, ApplicationStatus? newStatus, string comment,
        IReadOnlyCollection<string>? fileNames = null)
    {
        if (!citizen.EmailNotifications) return Task.CompletedTask;
        var lines = new List<string> { $"Здравейте, {citizen.FullName}!" };
        lines.Add(newStatus != null
            ? $"Статусът на заявление № {app.Number} е „{ApplicationStatuses.Name(newStatus.Value)}“."
            : $"Имате ново съобщение по заявление № {app.Number}.");
        if (comment.Length > 0) lines.Add($"Съобщение от служителя: „{comment}“");
        if (fileNames is { Count: > 0 })
            lines.Add(fileNames.Count == 1
                ? $"Служителят ви изпрати файл: {fileNames.First()}. Можете да го изтеглите от страницата на заявлението."
                : $"Служителят ви изпрати файлове: {string.Join(", ", fileNames)}. Можете да ги изтеглите от страницата на заявлението.");
        if (newStatus == ApplicationStatus.NeedsDocuments) lines.Add("Можете да качите документите от страницата на заявлението.");
        return _email.SendAsync(citizen.Email, $"Промяна по заявление № {app.Number}", lines,
            "Виж заявлението", _urls.Public($"/Applications/Details/{app.Id}"));
    }

    // Създава еднократна връзка. В базата остава само отпечатъкът ѝ.
    private async Task<string> CreateTokenAsync(Citizen citizen, string purpose, TimeSpan valid)
    {
        // Старите неизползвани връзки за същото действие спират да работят
        var old = await _db.CitizenTokens.Where(t => t.CitizenId == citizen.Id && t.Purpose == purpose && t.UsedAt == null).ToListAsync();
        foreach (var t in old) t.UsedAt = DateTime.Now;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _db.CitizenTokens.Add(new CitizenToken
        {
            CitizenId = citizen.Id, Purpose = purpose, TokenHash = HashToken(token), ExpiresAt = DateTime.Now.Add(valid)
        });
        await _db.SaveChangesAsync();
        return token;
    }

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // Намира валидна (неизползвана и неизтекла) връзка
    public static Task<CitizenToken?> FindTokenAsync(AppDbContext db, string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token)) return Task.FromResult<CitizenToken?>(null);
        var hash = HashToken(token);
        return db.CitizenTokens.Include(t => t.Citizen)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose && t.UsedAt == null && t.ExpiresAt > DateTime.Now);
    }
}

// Нови съобщения от служителя, които гражданинът още не е видял
public static class UnreadMessages
{
    public static IQueryable<Application> For(AppDbContext db, int citizenId) =>
        db.Applications.Where(a => a.CitizenId == citizenId &&
            a.Events.Any(e => !e.ByCitizen && e.Author != "" && (a.CitizenSeenAt == null || e.At > a.CitizenSeenAt)));

    // Заявления с нови съобщения от гражданина, които служителите още не са видели.
    // Ако заявлението още не е отваряно, ново е всичко след първото събитие (подаването).
    public static IQueryable<Application> ForStaff(AppDbContext db) =>
        db.Applications.Where(a => a.Events.Any(e => e.ByCitizen && e.Comment != "" &&
            (a.AdminSeenAt != null ? e.At > a.AdminSeenAt : e.At > a.Events.Min(x => x.At))));
}
