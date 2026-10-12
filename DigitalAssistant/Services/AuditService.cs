using DigitalAssistant.Data;
using DigitalAssistant.Models;

namespace DigitalAssistant.Services;

// Журнал на промените: кой администратор, кога и какво е променил (т. 3.1.5)
public static class AuditService
{
    public static void Log(AppDbContext db, string? admin, string action, string details)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            At = DateTime.Now,
            AdminUsername = admin ?? "",
            Action = action,
            Details = details
        });
    }
}
