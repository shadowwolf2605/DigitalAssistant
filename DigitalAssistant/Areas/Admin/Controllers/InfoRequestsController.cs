using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Служебни справки до институцията на служителя (т. 3.1.3). Служител от другата институция отговаря тук
// с текст и по желание файл, а служителят, който е попитал, вижда отговора в заявлението.
public class InfoRequestsController : AdminController
{
    protected override bool ForInstitutionStaff => true;

    private readonly AppDbContext _db;
    private readonly PersonalDataProtector _protector;

    public InfoRequestsController(AppDbContext db, PersonalDataProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    private Task<AdminUser?> MeAsync() =>
        _db.AdminUsers.Include(a => a.Institution).FirstOrDefaultAsync(a => a.Username == AdminName);

    // tab: waiting (чакат отговор), answered (отговорени), sent (изпратени от мен)
    public async Task<IActionResult> Index(string tab = "waiting")
    {
        var me = await MeAsync();
        var incoming = InfoRequests.ForStaff(_db, me);
        var query = tab switch
        {
            "answered" => incoming.Where(r => r.AnsweredAt != null),
            "sent" => _db.InfoRequests.Where(r => r.CreatedBy == AdminName),
            _ => incoming.Where(r => r.AnsweredAt == null)
        };
        if (tab is not ("answered" or "sent")) tab = "waiting";
        ViewBag.Tab = tab;
        ViewBag.Me = me;
        ViewBag.WaitingCount = await incoming.CountAsync(r => r.AnsweredAt == null);
        var list = await query.OrderByDescending(r => r.AnsweredAt ?? r.CreatedAt)
            .Select(r => new InfoRequest
            {
                Id = r.Id, ApplicationId = r.ApplicationId, ToInstitution = new Institution { Name = r.ToInstitution!.Name },
                Question = r.Question, CreatedBy = r.CreatedBy, FromInstitution = r.FromInstitution, CreatedAt = r.CreatedAt,
                Answer = r.Answer, AnsweredBy = r.AnsweredBy, AnsweredAt = r.AnsweredAt, AnswerFileName = r.AnswerFileName,
                Application = new Application { Number = r.Application!.Number, Service = new Service { Name = r.Application.Service!.Name } }
            }).ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Answer(int id)
    {
        var r = await LoadAsync(id);
        if (r == null) return NotFound();
        var me = await MeAsync();
        if (!InfoRequests.CanAnswer(me, r)) return NotFound();
        if (r.IsAnswered)
        {
            TempData["Error"] = $"На тази справка вече е отговорено от {r.AnsweredBy}.";
            return RedirectToAction(nameof(Index), new { tab = "answered" });
        }

        // Другата институция вижда само данните, нужни да намери човека, и записът остава в журнала
        AuditService.Log(_db, AdminName, "Отворена служебна справка", $"№ {r.Application!.Number} от {r.FromInstitution}: {r.Application.Citizen!.FullName}");
        await _db.SaveChangesAsync();
        ViewBag.Egn = _protector.Unprotect(r.Application.Citizen.EgnProtected);
        return View(r);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Лимитът е по-голям от позволения файл, за да може системата сама да каже, че файлът е голям, и да запази текста
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Answer(int id, string? answer, IFormFile? file)
    {
        var r = await _db.InfoRequests.Include(x => x.Application).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        var me = await MeAsync();
        if (!InfoRequests.CanAnswer(me, r)) return NotFound();
        if (r.IsAnswered)
        {
            TempData["Error"] = $"На тази справка вече е отговорено от {r.AnsweredBy}.";
            return RedirectToAction(nameof(Index));
        }

        var text = answer?.Trim() ?? "";
        var files = file == null ? new List<IFormFile>() : new List<IFormFile> { file };
        var errors = ApplicationFileRules.Check(files, required: false);
        if (text.Length < 2) errors.Insert(0, "Напишете отговор.");
        if (text.Length > 2000) errors.Insert(0, "Отговорът трябва да е до 2000 знака.");
        if (errors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", errors);
            TempData["AnswerDraft"] = text;
            return RedirectToAction(nameof(Answer), new { id });
        }

        var read = await ApplicationFileRules.ReadAsync(files);
        r.Answer = text;
        r.AnsweredBy = AdminName;
        r.AnsweredAt = DateTime.Now;
        if (read.Count > 0)
        {
            r.AnswerFileName = read[0].FileName;
            r.AnswerContentType = read[0].ContentType;
            r.AnswerFile = read[0].Data;
        }
        AuditService.Log(_db, AdminName, "Отговор на служебна справка",
            $"№ {r.Application!.Number} до {r.FromInstitution}" + (read.Count > 0 ? $", файл {read[0].FileName}" : ""));
        await _db.SaveChangesAsync();
        Success($"Отговорът е изпратен. Служителят ({r.CreatedBy}) ще го види в заявление № {r.Application.Number}.");
        return RedirectToAction(nameof(Index));
    }

    private Task<InfoRequest?> LoadAsync(int id) =>
        _db.InfoRequests.AsNoTracking()
            .Include(r => r.ToInstitution)
            .Include(r => r.Application).ThenInclude(a => a!.Citizen)
            .Include(r => r.Application).ThenInclude(a => a!.Service)
            .Where(r => r.Id == id)
            .Select(r => new InfoRequest
            {
                Id = r.Id, ApplicationId = r.ApplicationId, ToInstitutionId = r.ToInstitutionId, ToInstitution = r.ToInstitution,
                Question = r.Question, CreatedBy = r.CreatedBy, FromInstitution = r.FromInstitution, CreatedAt = r.CreatedAt,
                Answer = r.Answer, AnsweredBy = r.AnsweredBy, AnsweredAt = r.AnsweredAt, AnswerFileName = r.AnswerFileName,
                Application = r.Application
            })
            .FirstOrDefaultAsync();
}
