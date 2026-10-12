using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Controllers;

// Онлайн заявления на гражданите (т. 3.1.3).
// Гражданинът вижда само своите заявления и своите документи.
[Authorize(AuthenticationSchemes = CitizenAuth.Scheme)]
public class ApplicationsController : Controller
{
    private readonly AppDbContext _db;
    private readonly Notifications _notifications;
    private readonly PersonalDataProtector _protector;
    private readonly ExportService _export;

    public ApplicationsController(AppDbContext db, Notifications notifications, PersonalDataProtector protector, ExportService export)
    {
        _db = db;
        _notifications = notifications;
        _protector = protector;
        _export = export;
    }

    private int CitizenId => CitizenAuth.Id(User);

    public async Task<IActionResult> Index()
    {
        var list = await _db.Applications.Include(a => a.Service).Include(a => a.Events)
            .Where(a => a.CitizenId == CitizenId)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync();
        var citizen = await _db.Citizens.FirstAsync(c => c.Id == CitizenId);
        ViewBag.EmailConfirmed = citizen.EmailConfirmed;
        ViewBag.Email = citizen.Email;
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int serviceId, string? speed = null, string? city = null)
    {
        var service = await FindServiceAsync(serviceId);
        if (service == null) return NotFound();
        await FillCreateAsync(service);
        // Градът идва от помощника или е този от последното заявление на гражданина
        city = GuideCities.All.Any(c => c.Name == city) ? city
            : await _db.Applications.Where(a => a.CitizenId == CitizenId && a.City != "")
                .OrderByDescending(a => a.CreatedAt).Select(a => a.City).FirstOrDefaultAsync();
        return View(new ApplicationFormModel
        {
            ServiceId = serviceId, ResultDelivery = DeliveryOptions(service).First(),
            Express = speed == GuideState.Express && HasExpress(service),
            City = city
        });
    }

    // Данните на заявителя, които се показват над формуляра
    private async Task FillCreateAsync(Service service)
    {
        var citizen = await _db.Citizens.FirstAsync(c => c.Id == CitizenId);
        ViewBag.Service = service;
        ViewBag.Citizen = citizen;
        ViewBag.EgnMasked = Egn.Mask(_protector.Unprotect(citizen.EgnProtected));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Create(ApplicationFormModel model)
    {
        var service = await FindServiceAsync(model.ServiceId);
        if (service == null) return NotFound();

        if (model.ResultDelivery != null && !DeliveryOptions(service).Contains(model.ResultDelivery))
            ModelState.AddModelError(nameof(model.ResultDelivery), "Изберете начин на получаване от списъка.");
        if (model.City != null && !GuideCities.All.Any(c => c.Name == model.City))
            ModelState.AddModelError(nameof(model.City), "Изберете град от списъка.");
        if (model.ResultDelivery == "по пощата" && string.IsNullOrWhiteSpace(model.DeliveryAddress))
            ModelState.AddModelError(nameof(model.DeliveryAddress), "Въведете адрес за получаване по пощата.");
        foreach (var error in ApplicationFileRules.Check(model.Files, required: true))
            ModelState.AddModelError(nameof(model.Files), error);

        var citizen = await _db.Citizens.FirstAsync(c => c.Id == CitizenId);
        // Заявления се подават само с потвърден имейл, за да стигат известията до гражданина
        if (!citizen.EmailConfirmed)
            ModelState.AddModelError("", "Първо потвърдете имейла си от писмото, което ви изпратихме.");

        if (!ModelState.IsValid)
        {
            // Браузърът не пази избраните файлове, когато страницата се покаже отново
            if (model.Files.Count > 0 && ModelState[nameof(model.Files)]?.Errors.Count is null or 0)
                ModelState.AddModelError(nameof(model.Files), "Изберете файловете отново.");
            await FillCreateAsync(service);
            return View(model);
        }

        var app = new Application
        {
            CitizenId = citizen.Id,
            ServiceId = service.Id,
            Details = model.Details.Trim(),
            ResultDelivery = model.ResultDelivery!,
            DeliveryAddress = model.ResultDelivery == "по пощата" ? model.DeliveryAddress!.Trim() : "",
            City = model.City!,
            Express = model.Express && HasExpress(service),
            Fee = model.Express && HasExpress(service) ? service.ExpressFee ?? service.Fee : service.Fee,
            DeadlineDays = model.Express && HasExpress(service) ? service.ExpressDeadlineDays ?? service.DeadlineDays : service.DeadlineDays,
            Files = await ApplicationFileRules.ReadAsync(model.Files)
        };
        app.Events.Add(new ApplicationEvent { Status = ApplicationStatus.Submitted, Comment = "Заявлението е подадено.", Author = citizen.FullName, ByCitizen = true });
        app.CitizenSeenAt = DateTime.Now;
        _db.Applications.Add(app);
        await _db.SaveChangesAsync();

        // Номерът се прави от годината и поредния номер в базата, напр. 2026-00012
        app.Number = $"{app.CreatedAt:yyyy}-{app.Id:D5}";
        await _db.SaveChangesAsync();
        await _notifications.ApplicationSubmittedAsync(app, citizen, service.Name);

        TempData["Success"] = $"Заявлението е подадено успешно. Номерът му е № {app.Number}.";
        return RedirectToAction(nameof(Details), new { id = app.Id });
    }

    private static bool HasExpress(Service s) => s.ExpressDeadlineDays != null || s.ExpressFee != null;

    public async Task<IActionResult> Details(int id)
    {
        var app = await LoadAsync(id);
        if (app == null) return NotFound();

        // Кои съобщения са нови (след последното отваряне). След това заявлението се отбелязва като видяно.
        ViewBag.SeenBefore = app.CitizenSeenAt;
        // Часът се взима от приложението, а не от базата: в базата може да е в друга часова зона (UTC)
        var seenAt = DateTime.Now;
        await _db.Applications.Where(a => a.Id == id).ExecuteUpdateAsync(u => u.SetProperty(a => a.CitizenSeenAt, seenAt));
        ViewBag.Expected = ApplicationStatuses.ExpectedDate(app);
        ViewBag.Message = new CitizenMessageModel();
        // Кои документи служителят е поискал служебно от други институции (без съдържанието на отговора)
        ViewBag.InfoRequests = await _db.InfoRequests.Where(r => r.ApplicationId == id).OrderBy(r => r.CreatedAt)
            .Select(r => new InfoRequest
            {
                ToInstitution = new Institution { Name = r.ToInstitution!.Name },
                Question = r.Question, CreatedAt = r.CreatedAt, AnsweredAt = r.AnsweredAt
            }).ToListAsync();
        return View(app);
    }

    // Съобщение от гражданина до служителя, по желание с прикачени файлове
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Message(int id, CitizenMessageModel model, List<IFormFile>? files)
    {
        var app = await _db.Applications.Include(a => a.Citizen)
            .FirstOrDefaultAsync(a => a.Id == id && a.CitizenId == CitizenId);
        if (app == null) return NotFound();
        if (app.Status is ApplicationStatus.Completed or ApplicationStatus.Rejected)
        {
            TempData["Error"] = "Заявлението е приключило. За нов въпрос подайте ново заявление или се обадете в центъра.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Details), new { id });
        }

        files ??= new List<IFormFile>();
        var errors = ApplicationFileRules.Check(files, required: false);
        if (errors.Count > 0)
        {
            TempData["Error"] = "Съобщението не е изпратено. " + string.Join(" ", errors);
            return RedirectToAction(nameof(Details), new { id });
        }
        var newFiles = await ApplicationFileRules.ReadAsync(files);

        // Ако служителят е поискал документи и гражданинът ги изпраща със съобщението, заявлението отново отива за обработка
        if (newFiles.Count > 0 && app.Status == ApplicationStatus.NeedsDocuments)
            app.Status = ApplicationStatus.Submitted;
        var message = new ApplicationEvent
        {
            ApplicationId = app.Id, Status = app.Status, Comment = model.Text.Trim(), Author = app.Citizen!.FullName, ByCitizen = true
        };
        _db.ApplicationEvents.Add(message);
        app.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        if (newFiles.Count > 0)
        {
            // Файловете се показват до съобщението и в „Документи“
            foreach (var f in newFiles) { f.ApplicationId = app.Id; f.EventId = message.Id; _db.ApplicationFiles.Add(f); }
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = newFiles.Count > 0
            ? $"Съобщението и {(newFiles.Count == 1 ? "файлът" : $"{newFiles.Count} файла")} са изпратени до служителя."
            : "Съобщението е изпратено до служителя.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Потвърждение за подаденото заявление като PDF файл
    public async Task<IActionResult> Receipt(int id)
    {
        var app = await LoadAsync(id);
        if (app == null) return NotFound();
        app.Citizen = await _db.Citizens.AsNoTracking().FirstAsync(c => c.Id == CitizenId);
        var pdf = _export.ApplicationReceipt(app, Egn.Mask(_protector.Unprotect(app.Citizen.EgnProtected)), ApplicationStatuses.ExpectedDate(app));
        return File(pdf, "application/pdf", $"potvarzhdenie-{app.Number}.pdf");
    }

    // Качване на още документи, когато служителят ги е поискал
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> AddFiles(int id, List<IFormFile> files)
    {
        var app = await _db.Applications.Include(a => a.Citizen)
            .FirstOrDefaultAsync(a => a.Id == id && a.CitizenId == CitizenId);
        if (app == null) return NotFound();
        if (app.Status != ApplicationStatus.NeedsDocuments)
        {
            // Докато гражданинът е бил на страницата, служителят е сменил статуса
            TempData["Error"] = $"Документите не са качени, защото заявлението вече е „{ApplicationStatuses.Name(app.Status)}“. " +
                                "Ако все пак трябва да ги изпратите, напишете съобщение до служителя.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var errors = ApplicationFileRules.Check(files, required: true);
        if (errors.Count > 0)
        {
            TempData["Error"] = string.Join(" ", errors);
            return RedirectToAction(nameof(Details), new { id });
        }

        var newFiles = await ApplicationFileRules.ReadAsync(files);
        app.Status = ApplicationStatus.Submitted;
        app.UpdatedAt = DateTime.Now;
        var uploaded = new ApplicationEvent
        {
            ApplicationId = app.Id, Status = ApplicationStatus.Submitted, Author = app.Citizen!.FullName, ByCitizen = true,
            Comment = $"Качени са допълнителни документи ({newFiles.Count})."
        };
        _db.ApplicationEvents.Add(uploaded);
        await _db.SaveChangesAsync();
        // Файловете се показват и до съобщението, за да вижда служителят кои са новите
        foreach (var f in newFiles) { f.ApplicationId = app.Id; f.EventId = uploaded.Id; _db.ApplicationFiles.Add(f); }
        await _db.SaveChangesAsync();
        TempData["Success"] = "Документите са качени. Заявлението отново е изпратено за обработка.";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> File(int id, int fileId)
    {
        var file = await _db.ApplicationFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && f.ApplicationId == id && f.Application!.CitizenId == CitizenId);
        if (file == null) return NotFound();
        return File(file.Data, file.ContentType, file.FileName);
    }

    private Task<Service?> FindServiceAsync(int id) =>
        _db.Services.Include(s => s.Institution).FirstOrDefaultAsync(s => s.Id == id && s.IsActive && s.AllowOnline);

    private async Task<Application?> LoadAsync(int id)
    {
        var app = await _db.Applications.AsNoTracking()
            .Include(a => a.Service).ThenInclude(s => s!.Institution)
            .Include(a => a.Events)
            .FirstOrDefaultAsync(a => a.Id == id && a.CitizenId == CitizenId);
        if (app == null) return null;
        // Списък с файловете без самото им съдържание
        app.Files = await _db.ApplicationFiles.Where(f => f.ApplicationId == id)
            .Select(f => new ApplicationFile { Id = f.Id, FileName = f.FileName, Size = f.Size, UploadedAt = f.UploadedAt, ByStaff = f.ByStaff, EventId = f.EventId })
            .ToListAsync();
        return app;
    }

    public static List<string> DeliveryOptions(Service s)
    {
        var list = s.ResultDelivery.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        return list.Count > 0 ? list : ServiceFormModel.DeliveryOptions.ToList();
    }
}
