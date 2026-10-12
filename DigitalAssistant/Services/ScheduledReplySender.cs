using DigitalAssistant.Data;
using DigitalAssistant.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Services;

// Изпраща насрочените отговори на служителите, когато дойдат денят и часът им. Чака до най-близкия насрочен час
// (но поне веднъж на час проверява отново), а при ново или отменено насрочване се събужда веднага.
// Така базата не се отваря без нужда и безплатната база в интернет може да „заспива“.
// Ако системата е била спряна в този час, отговорът се изпраща веднага след стартирането ѝ.
public class ScheduledReplySender : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ScheduledReplySender> _logger;

    public ScheduledReplySender(IServiceProvider services, ILogger<ScheduledReplySender> logger)
    {
        _services = services;
        _logger = logger;
    }

    private readonly SemaphoreSlim _wake = new(0);

    // Вика се, когато служител насрочи или отмени отговор
    public void Wake() => _wake.Release();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = TimeSpan.FromHours(1);
            try
            {
                var next = await SendDueAsync(stoppingToken);
                if (next != null && next.Value - DateTime.Now < wait)
                    wait = next.Value - DateTime.Now;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Насрочените отговори не бяха изпратени.");
                wait = TimeSpan.FromMinutes(1);
            }
            if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
            await _wake.WaitAsync(wait, stoppingToken);
            while (_wake.CurrentCount > 0) _wake.Wait(0);
        }
    }

    // Изпраща дошлите отговори и връща часа на следващия насрочен
    private async Task<DateTime?> SendDueAsync(CancellationToken token)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<Notifications>();
        var now = DateTime.Now;
        var due = await db.ScheduledReplies.Where(r => r.SendAt <= now).OrderBy(r => r.SendAt)
            .Select(r => r.Id).ToListAsync(token);
        foreach (var id in due)
        {
            try
            {
                await SendOneAsync(db, notifications, id, token);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Служителят го е отменил точно в този момент – нищо не се изпраща
                db.ChangeTracker.Clear();
                _logger.LogInformation("Насроченият отговор {Id} е отменен преди изпращането.", id);
            }
        }
        return await db.ScheduledReplies.OrderBy(r => r.SendAt).Select(r => (DateTime?)r.SendAt).FirstOrDefaultAsync(token);
    }

    private async Task SendOneAsync(AppDbContext db, Notifications notifications, int id, CancellationToken token)
    {
        var reply = await db.ScheduledReplies.Include(r => r.Files).FirstOrDefaultAsync(r => r.Id == id, token);
        if (reply == null) return;  // отменен междувременно
        var app = await db.Applications.Include(a => a.Citizen).Include(a => a.Events)
            .FirstAsync(a => a.Id == reply.ApplicationId, token);
        var files = reply.Files.Select(f => new ApplicationFile
        {
            FileName = f.FileName, ContentType = f.ContentType, Size = f.Size, Data = f.Data
        }).ToList();
        var note = $". Насрочен за {reply.SendAt:dd.MM.yyyy HH:mm}";

        // Ако след насрочването статусът е сменен (напр. заявлението е отказано или готово),
        // насроченият отговор не го връща назад – изпраща се само съобщението и файловете.
        var status = reply.Status;
        if (app.Status != ApplicationStatuses.StatusAt(app, reply.CreatedAt))
        {
            status = app.Status;
            note += $". Статусът не е сменен на „{ApplicationStatuses.Name(reply.Status)}“, защото междувременно е станал „{ApplicationStatuses.Name(app.Status)}“";
        }

        db.ScheduledReplies.Remove(reply);
        if (status == app.Status && reply.Comment.Length == 0 && files.Count == 0)
        {
            // Остава само смяна на статуса, която вече не е нужна
            AuditService.Log(db, reply.CreatedBy, "Неизпратен насрочен отговор", $"№ {app.Number}" + note);
            await db.SaveChangesAsync(token);
            return;
        }
        await StaffReplies.SendAsync(db, notifications, app, status, reply.Comment, files, reply.CreatedBy, note);
        _logger.LogInformation("Изпратен е насроченият отговор по заявление № {Number}", app.Number);
    }
}
