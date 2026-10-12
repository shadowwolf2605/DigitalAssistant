using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Обработка на онлайн заявленията (т. 3.1.3).
// Всяко отваряне на заявление, изтегляне на документ и смяна на статус се записва в журнала (т. 3.1.5).
public class ApplicationsController : AdminController
{
    private readonly AppDbContext _db;
    private readonly PersonalDataProtector _protector;
    private readonly Notifications _notifications;
    private readonly ScheduledReplySender _sender;

    public ApplicationsController(AppDbContext db, PersonalDataProtector protector, Notifications notifications, ScheduledReplySender sender)
    {
        _db = db;
        _protector = protector;
        _notifications = notifications;
        _sender = sender;
    }

    // Служител с избран град отваря само заявленията от този град
    protected override async Task<bool> AllowedAsync(ActionExecutingContext context)
    {
        if (AdminCity == null) return true;
        int? id = context.ActionArguments.TryGetValue("id", out var value) && value is int x ? x
            : context.ActionArguments.Values.OfType<StatusChangeModel>().FirstOrDefault()?.ApplicationId;
        return id == null || await _db.Applications.AnyAsync(a => a.Id == id && a.City == AdminCity);
    }

    public async Task<IActionResult> Index(ApplicationStatus? status, string? q, string? city, bool messages = false, bool overdue = false)
    {
        var query = (messages ? UnreadMessages.ForStaff(_db, AdminCity) : InMyCity(_db.Applications))
            .Include(a => a.Citizen).Include(a => a.Service).Include(a => a.Events).AsQueryable();
        if (status != null) query = query.Where(a => a.Status == status);
        // Служител за всички градове може да види заявленията само от един град
        if (AdminCity == null && !string.IsNullOrEmpty(city)) query = query.Where(a => a.City == city);
        var list = await query.OrderByDescending(a => a.UpdatedAt).ToListAsync();

        // Търсене по номер, име на гражданин, услуга или ЕГН (без значение малки/главни букви)
        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim().TrimStart('№').Trim();
            var egnHash = Egn.Validate(text) == null ? _protector.Hash(text) : null;
            list = list.Where(a => a.Number.Contains(text, StringComparison.OrdinalIgnoreCase)
                                   || a.Citizen!.FullName.Contains(text, StringComparison.OrdinalIgnoreCase)
                                   || a.Service!.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                                   || (egnHash != null && a.Citizen.EgnHash == egnHash)).ToList();
        }
        // Закъснели: срокът е минал, а заявлението още е „Подадено“ или „В обработка“
        if (overdue) list = list.Where(a => ApplicationStatuses.ExpectedDate(a) < DateTime.Today).ToList();
        ViewBag.Overdue = overdue;
        ViewBag.Messages = messages;
        ViewBag.MessagesCount = await UnreadMessages.ForStaff(_db, AdminCity).CountAsync();
        ViewBag.City = AdminCity == null ? city : null;
        ViewBag.MyCity = AdminCity;
        ViewBag.Cities = AdminCity == null ? await _db.Applications.Where(a => a.City != "").Select(a => a.City).Distinct().OrderBy(c => c).ToListAsync() : new List<string>();

        ViewBag.Status = status;
        ViewBag.Query = q;
        ViewBag.Counts = await InMyCity(_db.Applications).GroupBy(a => a.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        return View(list);
    }

    public async Task<IActionResult> Details(int id)
    {
        // Само за четене, за да не се запише списъкът с файлове (без съдържание) обратно в базата
        var app = await _db.Applications.AsNoTracking()
            .Include(a => a.Citizen)
            .Include(a => a.Service).ThenInclude(s => s!.Institution)
            .Include(a => a.Events)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        app.Files = await _db.ApplicationFiles.Where(f => f.ApplicationId == id)
            .Select(f => new ApplicationFile { Id = f.Id, FileName = f.FileName, Size = f.Size, UploadedAt = f.UploadedAt, ByStaff = f.ByStaff, EventId = f.EventId })
            .ToListAsync();

        AuditService.Log(_db, AdminName, "Отворено заявление", $"№ {app.Number} на {app.Citizen!.FullName}");
        await _db.SaveChangesAsync();

        // Новите съобщения от гражданина се отбелязват, след това заявлението се смята за видяно
        ViewBag.SeenBefore = app.AdminSeenAt;
        // Часът се взима от приложението, а не от базата: в базата може да е в друга часова зона (UTC)
        var seenAt = DateTime.Now;
        await _db.Applications.Where(a => a.Id == id).ExecuteUpdateAsync(u => u.SetProperty(a => a.AdminSeenAt, seenAt));
        ViewBag.Egn = _protector.Unprotect(app.Citizen.EgnProtected);
        ViewBag.Expected = ApplicationStatuses.ExpectedDate(app);
        // Насрочените отговори (без съдържанието на файловете)
        ViewBag.Scheduled = await _db.ScheduledReplies.Where(r => r.ApplicationId == id).OrderBy(r => r.SendAt)
            .Select(r => new ScheduledReply
            {
                Id = r.Id, Status = r.Status, Comment = r.Comment, SendAt = r.SendAt, CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt,
                Files = r.Files.Select(f => new ScheduledReplyFile { FileName = f.FileName, Size = f.Size }).ToList()
            }).ToListAsync();

        // Служебните справки до други институции по това заявление (без файловете)
        ViewBag.InfoRequests = await _db.InfoRequests.Where(r => r.ApplicationId == id).OrderByDescending(r => r.CreatedAt)
            .Select(r => new InfoRequest
            {
                Id = r.Id, ToInstitutionId = r.ToInstitutionId, ToInstitution = new Institution { Name = r.ToInstitution!.Name },
                Question = r.Question, CreatedBy = r.CreatedBy, FromInstitution = r.FromInstitution, CreatedAt = r.CreatedAt,
                Answer = r.Answer, AnsweredBy = r.AnsweredBy, AnsweredAt = r.AnsweredAt, AnswerFileName = r.AnswerFileName
            }).ToListAsync();
        ViewBag.Institutions = await _db.Institutions.OrderBy(i => i.Name).ToListAsync();
        // Другите заявления на същия гражданин, подадени към други институции – документите в тях не се искат отново
        ViewBag.OtherApplications = await InMyCity(_db.Applications).Where(a => a.CitizenId == app.CitizenId && a.Id != id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new Application
            {
                Id = a.Id, Number = a.Number, Status = a.Status, CreatedAt = a.CreatedAt,
                Service = new Service { Name = a.Service!.Name, Institution = new Institution { Name = a.Service.Institution!.Name } },
                Files = a.Files.Where(f => !f.ByStaff).Select(f => new ApplicationFile { Id = f.Id, FileName = f.FileName, Size = f.Size }).ToList()
            }).ToListAsync();

        // По подразбиране насроченият отговор е за утре в 9:00. След грешка формата се попълва отново.
        var model = new StatusChangeModel { ApplicationId = app.Id, Status = app.Status, SendAt = DateTime.Today.AddDays(1).AddHours(9) };
        if (TempData["Draft"] is string json && JsonSerializer.Deserialize<ReplyDraft>(json) is { } draft)
        {
            model.Status = draft.Status;
            model.Comment = draft.Comment;
            model.SendLater = draft.SendLater;
            model.SendAt = draft.SendAt ?? model.SendAt;
        }
        return View(model.WithApplication(app));
    }

    // Връща към заявлението с грешката, без да се губи написаното
    private IActionResult BackWithDraft(int id, StatusChangeModel model, string error)
    {
        TempData["Error"] = error + (model.Files.Count > 0 ? " Изберете файловете отново." : "");
        TempData["Draft"] = JsonSerializer.Serialize(new ReplyDraft(model.Status, model.Comment, model.SendLater, model.SendAt));
        return RedirectToAction(nameof(Details), new { id });
    }

    private record ReplyDraft(ApplicationStatus Status, string? Comment, bool SendLater, DateTime? SendAt);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> ChangeStatus(StatusChangeModel model)
    {
        var app = await _db.Applications.Include(a => a.Citizen).FirstOrDefaultAsync(a => a.Id == model.ApplicationId);
        if (app == null) return NotFound();

        if (!ModelState.IsValid)
        {
            return BackWithDraft(app.Id, model, string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
        }

        // Страницата е отворена отдавна и статусът е сменен междувременно (от друг служител, от насрочен отговор
        // или от гражданина, който е качил документи). Ако служителят не е пипал статуса, той остава текущият.
        if (model.CurrentStatus != null && model.CurrentStatus != app.Status)
        {
            if (model.Status == model.CurrentStatus)
                model.Status = app.Status;
            else
            {
                model.CurrentStatus = app.Status;
                return BackWithDraft(app.Id, model, $"Междувременно статусът е сменен на „{ApplicationStatuses.Name(app.Status)}“. " +
                                                    "Проверете историята и изпратете отговора отново.");
            }
        }

        var comment = model.Comment?.Trim() ?? "";
        if ((model.Status == ApplicationStatus.NeedsDocuments || model.Status == ApplicationStatus.Rejected) && comment.Length == 0)
        {
            return BackWithDraft(app.Id, model, model.Status == ApplicationStatus.Rejected
                ? "Напишете причината за отказа."
                : "Напишете кои документи липсват.");
        }
        var fileErrors = ApplicationFileRules.Check(model.Files, required: false);
        if (fileErrors.Count > 0)
        {
            return BackWithDraft(app.Id, model, string.Join(" ", fileErrors));
        }
        var newFiles = await ApplicationFileRules.ReadAsync(model.Files);
        if (model.Status == app.Status && comment.Length == 0 && newFiles.Count == 0)
        {
            return BackWithDraft(app.Id, model, "Изберете нов статус, напишете съобщение или прикачете файл за гражданина.");
        }

        var filesText = newFiles.Count switch
        {
            0 => "",
            1 => " Файлът е прикачен.",
            _ => $" Прикачени са {newFiles.Count} файла."
        };

        // Насрочен отговор: пази се отделно и гражданинът не го вижда, докато не дойде часът
        if (model.SendLater)
        {
            var now = DateTime.Now;
            if (model.SendAt == null || model.SendAt <= now)
            {
                return BackWithDraft(app.Id, model, model.SendAt == null ? "Изберете дата и час на изпращане." : "Датата и часът трябва да са в бъдещето.");
            }
            if (model.SendAt > now.AddYears(1))
            {
                return BackWithDraft(app.Id, model, "Отговорът може да се насрочи най-много една година напред.");
            }
            var sendAt = new DateTime(model.SendAt.Value.Ticks / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute);
            _db.ScheduledReplies.Add(new ScheduledReply
            {
                ApplicationId = app.Id, Status = model.Status, Comment = comment, SendAt = sendAt, CreatedBy = AdminName,
                Files = newFiles.Select(f => new ScheduledReplyFile
                {
                    FileName = f.FileName, ContentType = f.ContentType, Size = f.Size, Data = f.Data
                }).ToList()
            });
            AuditService.Log(_db, AdminName, "Насрочен отговор до гражданин",
                $"№ {app.Number}: за {sendAt:dd.MM.yyyy HH:mm}, статус „{ApplicationStatuses.Name(model.Status)}“" +
                (comment.Length > 0 ? $". Съобщение: „{comment}“" : "") +
                (newFiles.Count > 0 ? $". Файлове: {string.Join(", ", newFiles.Select(f => f.FileName))}" : ""));
            await _db.SaveChangesAsync();
            _sender.Wake();
            Success($"Отговорът е насрочен за {sendAt:dd.MM.yyyy} в {sendAt:HH:mm} ч. Дотогава гражданинът не го вижда и можете да го отмените." + filesText);
            TempData["ReplySaved"] = true;
            return RedirectToAction(nameof(Details), new { id = app.Id });
        }

        var oldStatus = await StaffReplies.SendAsync(_db, _notifications, app, model.Status, comment, newFiles, AdminName);
        // Насрочен по-рано отговор вече няма да смени статуса обратно – служителят се предупреждава
        var pending = oldStatus == model.Status ? null
            : await _db.ScheduledReplies.Where(r => r.ApplicationId == app.Id).OrderBy(r => r.SendAt).Select(r => (DateTime?)r.SendAt).FirstOrDefaultAsync();
        var pendingText = pending == null ? ""
            : $" Има насрочен отговор за {pending:dd.MM.yyyy HH:mm}. Той ще изпрати само съобщението, без да сменя статуса. Ако не е нужен, отменете го.";
        Success((oldStatus == model.Status
            ? $"Съобщението до {app.Citizen!.FullName} е изпратено."
            : $"Статусът на заявление № {app.Number} е „{ApplicationStatuses.Name(model.Status)}“." +
              (app.Citizen!.EmailNotifications ? " Гражданинът ще получи имейл." : " Гражданинът е изключил имейлите и ще види промяната в профила си."))
            + filesText + pendingText);
        TempData["ReplySaved"] = true;
        return RedirectToAction(nameof(Details), new { id = app.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelScheduled(int id, int replyId)
    {
        var reply = await _db.ScheduledReplies.Include(r => r.Application)
            .FirstOrDefaultAsync(r => r.Id == replyId && r.ApplicationId == id);
        if (reply == null)
        {
            TempData["Error"] = "Този отговор вече е изпратен или отменен.";
            return RedirectToAction(nameof(Details), new { id });
        }
        _db.ScheduledReplies.Remove(reply);
        AuditService.Log(_db, AdminName, "Отменен насрочен отговор",
            $"№ {reply.Application!.Number}: беше за {reply.SendAt:dd.MM.yyyy HH:mm}");
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Точно в този момент е дошъл часът и отговорът е изпратен
            TempData["Error"] = "Този отговор вече е изпратен или отменен.";
            return RedirectToAction(nameof(Details), new { id });
        }
        Success("Насроченият отговор е отменен и няма да бъде изпратен.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // Служебна справка: служителят иска информация или документ от друга институция, вместо гражданинът да ходи там
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AskInstitution(int id, int? toInstitutionId, string? question)
    {
        var app = await _db.Applications.Include(a => a.Service).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        var institution = toInstitutionId == null ? null : await _db.Institutions.FindAsync(toInstitutionId);
        var text = question?.Trim() ?? "";
        string? error = institution == null ? "Изберете институция."
            : institution.Id == app.Service!.InstitutionId ? "Заявлението вече е за тази институция. Изберете друга."
            : text.Length < 5 ? "Напишете какво искате от институцията."
            : text.Length > 1000 ? "Текстът трябва да е до 1000 знака." : null;
        if (error != null)
        {
            TempData["Error"] = error;
            TempData["AskDraft"] = text;
            TempData["AskInstitution"] = toInstitutionId;
            return RedirectToAction(nameof(Details), null, new { id }, "infoRequests");
        }

        var me = await _db.AdminUsers.Include(a => a.Institution).FirstOrDefaultAsync(a => a.Username == AdminName);
        _db.InfoRequests.Add(new InfoRequest
        {
            ApplicationId = app.Id, ToInstitutionId = institution!.Id, Question = text, CreatedBy = AdminName,
            FromInstitution = me?.Institution?.Name ?? InfoRequests.CenterName
        });
        AuditService.Log(_db, AdminName, "Служебна справка", $"№ {app.Number} до {institution.Name}: „{text}“");
        await _db.SaveChangesAsync();
        Success($"Справката до {institution.Name} е изпратена. Ще видите отговора тук, когато служител от там отговори.");
        return RedirectToAction(nameof(Details), null, new { id }, "infoRequests");
    }

    // Файл от отговора на другата институция
    public async Task<IActionResult> InfoFile(int id, int requestId)
    {
        var r = await _db.InfoRequests.Include(x => x.Application)
            .FirstOrDefaultAsync(x => x.Id == requestId && x.ApplicationId == id && x.AnswerFile != null);
        if (r == null) return NotFound();
        AuditService.Log(_db, AdminName, "Изтеглен документ от служебна справка", $"№ {r.Application!.Number}: {r.AnswerFileName}");
        await _db.SaveChangesAsync();
        return File(r.AnswerFile!, r.AnswerContentType ?? "application/octet-stream", r.AnswerFileName);
    }

    public async Task<IActionResult> File(int id, int fileId)
    {
        var file = await _db.ApplicationFiles.Include(f => f.Application)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.ApplicationId == id);
        if (file == null) return NotFound();
        AuditService.Log(_db, AdminName, "Изтеглен документ", $"№ {file.Application!.Number}: {file.FileName}");
        await _db.SaveChangesAsync();
        return File(file.Data, file.ContentType, file.FileName);
    }
}
