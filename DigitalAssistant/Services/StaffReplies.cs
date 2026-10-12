using DigitalAssistant.Data;
using DigitalAssistant.Models;

namespace DigitalAssistant.Services;

// Отговор на служителя по заявление: нов статус, съобщение и файлове към гражданина.
// Използва се при „Веднага“ и когато дойде часът на насрочен отговор.
public static class StaffReplies
{
    // app трябва да е зареден заедно с гражданина (Include Citizen). Връща предишния статус.
    // Съобщенията на гражданина не се отбелязват като видени: той може да е писал, докато служителят
    // е пишел отговора. Те стават видени, когато служителят отвори заявлението.
    public static async Task<ApplicationStatus> SendAsync(AppDbContext db, Notifications notifications, Application app,
        ApplicationStatus status, string comment, List<ApplicationFile> files, string admin, string auditNote = "")
    {
        // Съобщението и файловете се записват заедно – или всичко, или нищо
        await using var transaction = await db.Database.BeginTransactionAsync();
        var oldStatus = app.Status;
        app.Status = status;
        app.UpdatedAt = DateTime.Now;
        var ev = new ApplicationEvent
        {
            ApplicationId = app.Id, Status = status, Comment = comment, Author = admin
        };
        db.ApplicationEvents.Add(ev);
        await db.SaveChangesAsync();

        // Файловете от служителя се пазят при документите на заявлението и се свързват със съобщението
        foreach (var f in files)
        {
            f.ApplicationId = app.Id;
            f.ByStaff = true;
            f.EventId = ev.Id;
            db.ApplicationFiles.Add(f);
        }
        var fileNames = files.Select(f => f.FileName).ToList();
        AuditService.Log(db, admin, oldStatus == status ? "Съобщение до гражданин" : "Сменен статус на заявление",
            $"№ {app.Number}: „{ApplicationStatuses.Name(oldStatus)}“ → „{ApplicationStatuses.Name(status)}“" +
            (comment.Length > 0 ? $". Съобщение: „{comment}“" : "") +
            (fileNames.Count > 0 ? $". Файлове: {string.Join(", ", fileNames)}" : "") + auditNote);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        // Гражданинът получава имейл за промяната
        await notifications.StaffUpdateAsync(app, app.Citizen!, oldStatus == status ? null : status, comment, fileNames);
        return oldStatus;
    }
}
