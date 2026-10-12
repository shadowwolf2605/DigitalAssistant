using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Добавяне, промяна и премахване на услуги (т. 3.1.1)
public class ServicesController : AdminController
{
    private const long MaxFileSize = 5 * 1024 * 1024;
    private static readonly Dictionary<string, string> AllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword"
    };

    private readonly AppDbContext _db;

    public ServicesController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(ServiceFilterModel filter)
    {
        var query = _db.Services.Include(s => s.Category).Include(s => s.Institution).AsQueryable();
        if (filter.CategoryId != null) query = query.Where(s => s.CategoryId == filter.CategoryId);
        if (filter.InstitutionId != null) query = query.Where(s => s.InstitutionId == filter.InstitutionId);
        if (filter.Status == "active") query = query.Where(s => s.IsActive);
        if (filter.Status == "inactive") query = query.Where(s => !s.IsActive);

        var services = await query.ToListAsync();
        if (!string.IsNullOrWhiteSpace(filter.Name))
            services = services.Where(s => s.Name.Contains(filter.Name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        await FillListsAsync(filter.CategoryId, filter.InstitutionId);
        ViewBag.Filter = filter;
        return View(services.OrderBy(s => s.Name).ToList());
    }

    public async Task<IActionResult> Create()
    {
        await FillListsAsync(null, null);
        return View("Form", new ServiceFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServiceFormModel model)
    {
        await ValidateAsync(model);
        if (!ModelState.IsValid)
        {
            await FillListsAsync(model.CategoryId, model.InstitutionId);
            return View("Form", model);
        }

        var service = new Service();
        await CopyAsync(model, service);
        _db.Services.Add(service);
        AuditService.Log(_db, AdminName, "Добавена услуга", service.Name);
        await _db.SaveChangesAsync();

        Success($"Услугата „{service.Name}“ е добавена.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var s = await _db.Services.FindAsync(id);
        if (s == null) return NotFound();

        var model = new ServiceFormModel
        {
            Id = s.Id, Name = s.Name, Description = s.Description, CategoryId = s.CategoryId,
            InstitutionId = s.InstitutionId, RequiredDocuments = s.RequiredDocuments, OfficialDocuments = s.OfficialDocuments, Steps = s.Steps,
            Question = s.Question, QuestionOptions = s.QuestionOptions,
            Address = s.Address, PlaceSearch = s.PlaceSearch, WorkingHours = s.WorkingHours, Phone = s.Phone, Fee = s.Fee,
            ExpressFee = s.ExpressFee, DeadlineDays = s.DeadlineDays, ExpressDeadlineDays = s.ExpressDeadlineDays,
            ResultDelivery = s.ResultDelivery.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
            Keywords = s.Keywords, ExistingTemplateName = s.TemplateFileName, AllowOnline = s.AllowOnline
        };
        ViewBag.IsActive = s.IsActive;
        await FillListsAsync(s.CategoryId, s.InstitutionId);
        return View("Form", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ServiceFormModel model)
    {
        var service = await _db.Services.Include(s => s.Category).Include(s => s.Institution).Include(s => s.Template)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (service == null) return NotFound();
        model.Id = id;
        model.ExistingTemplateName = service.TemplateFileName;

        await ValidateAsync(model);
        if (!ModelState.IsValid)
        {
            ViewBag.IsActive = service.IsActive;
            await FillListsAsync(model.CategoryId, model.InstitutionId);
            return View("Form", model);
        }

        var categories = await _db.Categories.ToDictionaryAsync(c => c.Id, c => c.Name);
        var institutions = await _db.Institutions.ToDictionaryAsync(i => i.Id, i => i.Name);
        var before = Snapshot(service, categories, institutions);
        await CopyAsync(model, service);
        var after = Snapshot(service, categories, institutions);
        var changes = before.Keys.Where(k => before[k] != after[k])
            .Select(k => $"{k}: „{Short(before[k])}“ → „{Short(after[k])}“").ToList();
        // Нов файл с образеца със същото име също е промяна
        if (model.TemplateUpload is { Length: > 0 } && before["Образец"] == after["Образец"])
            changes.Add($"Образец: качен нов файл „{after["Образец"]}“");

        if (changes.Count > 0)
        {
            AuditService.Log(_db, AdminName, "Променена услуга", $"{service.Name}. " + string.Join("; ", changes));
            await _db.SaveChangesAsync();
            Success($"Промените по „{service.Name}“ са запазени.");
        }
        else
        {
            Success("Няма промени за запазване.");
        }
        return RedirectToAction(nameof(Index));
    }

    // При премахване услугата не се изтрива, а става неактивна
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        var service = await _db.Services.FindAsync(id);
        if (service == null) return NotFound();
        service.IsActive = false;
        service.UpdatedAt = DateTime.Now;
        AuditService.Log(_db, AdminName, "Премахната услуга (неактивна)", service.Name);
        await _db.SaveChangesAsync();
        Success($"Услугата „{service.Name}“ е премахната и вече не се вижда от гражданите.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int id)
    {
        var service = await _db.Services.FindAsync(id);
        if (service == null) return NotFound();
        service.IsActive = true;
        service.UpdatedAt = DateTime.Now;
        AuditService.Log(_db, AdminName, "Върната услуга (активна)", service.Name);
        await _db.SaveChangesAsync();
        Success($"Услугата „{service.Name}“ отново е активна.");
        return RedirectToAction(nameof(Index));
    }

    // Проверки при запазване на формуляра
    private async Task ValidateAsync(ServiceFormModel model)
    {
        var keywords = model.Keywords?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(model.Keywords) && keywords.Length == 0)
            ModelState.AddModelError(nameof(model.Keywords), "Въведете поне една ключова дума.");

        if (model.InstitutionId != null && !string.IsNullOrWhiteSpace(model.Name))
        {
            var name = model.Name.Trim();
            var sameInstitution = await _db.Services
                .Where(s => s.InstitutionId == model.InstitutionId && s.Id != model.Id)
                .Select(s => s.Name).ToListAsync();
            if (sameInstitution.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                ModelState.AddModelError(nameof(model.Name), "Вече има услуга с това име в същата институция.");
        }

        if (!ServiceFormModel.DeliveryOptions.Any(o => model.ResultDelivery.Contains(o)))
            ModelState.AddModelError(nameof(model.ResultDelivery), "Изберете поне един начин на получаване.");
        if (!string.IsNullOrWhiteSpace(model.Question))
        {
            var answers = ServiceText.Situations(model.QuestionOptions);
            if (answers.Count < 2)
                ModelState.AddModelError(nameof(model.QuestionOptions), "Напишете поне 2 отговора, всеки на нов ред.");
            else if (answers.Count > 6)
                ModelState.AddModelError(nameof(model.QuestionOptions), "Отговорите трябва да са най-много 6.");
            else if (answers.Any(a => a.Answer.Length > 80))
                ModelState.AddModelError(nameof(model.QuestionOptions), "Всеки отговор (преди първата „|“) трябва да е до 80 знака.");
        }
        else if (!string.IsNullOrWhiteSpace(model.QuestionOptions))
            ModelState.AddModelError(nameof(model.Question), "Напишете въпроса или изтрийте отговорите.");
        if (model.ExpressDeadlineDays != null && model.DeadlineDays != null && model.ExpressDeadlineDays >= model.DeadlineDays)
            ModelState.AddModelError(nameof(model.ExpressDeadlineDays), "Срокът за бърза услуга трябва да е по-кратък от обикновения.");

        if (model.CategoryId != null && !await _db.Categories.AnyAsync(c => c.Id == model.CategoryId))
            ModelState.AddModelError(nameof(model.CategoryId), "Изберете категория.");
        if (model.InstitutionId != null && !await _db.Institutions.AnyAsync(i => i.Id == model.InstitutionId))
            ModelState.AddModelError(nameof(model.InstitutionId), "Изберете институция.");

        if (model.TemplateUpload != null && model.TemplateUpload.Length > 0)
        {
            var ext = Path.GetExtension(model.TemplateUpload.FileName);
            if (!AllowedFiles.ContainsKey(ext))
                ModelState.AddModelError(nameof(model.TemplateUpload), "Образецът трябва да е файл .pdf или .doc.");
            else if (model.TemplateUpload.Length > MaxFileSize)
                ModelState.AddModelError(nameof(model.TemplateUpload), "Файлът е по-голям от 5 MB.");
        }
    }

    private async Task CopyAsync(ServiceFormModel m, Service s)
    {
        s.Name = m.Name.Trim();
        s.Description = m.Description?.Trim() ?? "";
        s.CategoryId = m.CategoryId!.Value;
        s.InstitutionId = m.InstitutionId!.Value;
        s.RequiredDocuments = m.RequiredDocuments.Trim();
        s.OfficialDocuments = m.OfficialDocuments?.Trim() ?? "";
        s.Steps = m.Steps?.Trim() ?? "";
        s.Question = m.Question?.Trim() ?? "";
        s.QuestionOptions = s.Question.Length > 0 ? m.QuestionOptions?.Trim() ?? "" : "";
        s.Address = m.Address.Trim();
        s.PlaceSearch = m.PlaceSearch?.Trim() ?? "";
        s.WorkingHours = m.WorkingHours?.Trim() ?? "";
        s.Phone = m.Phone?.Trim() ?? "";
        s.Fee = m.Fee!.Value;
        s.ExpressFee = m.ExpressFee;
        s.DeadlineDays = m.DeadlineDays!.Value;
        s.ExpressDeadlineDays = m.ExpressDeadlineDays;
        s.ResultDelivery = string.Join(", ", ServiceFormModel.DeliveryOptions.Where(o => m.ResultDelivery.Contains(o)));
        s.Keywords = string.Join(", ", m.Keywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        s.AllowOnline = m.AllowOnline;
        s.UpdatedAt = DateTime.Now;

        if (m.RemoveTemplate)
        {
            s.Template = null;
            s.TemplateFileName = null;
            s.TemplateContentType = null;
        }
        if (m.TemplateUpload != null && m.TemplateUpload.Length > 0)
        {
            using var ms = new MemoryStream();
            await m.TemplateUpload.CopyToAsync(ms);
            var ext = Path.GetExtension(m.TemplateUpload.FileName);
            if (s.Template == null) s.Template = new ServiceTemplate { File = ms.ToArray() };
            else s.Template.File = ms.ToArray();
            s.TemplateFileName = Path.GetFileName(m.TemplateUpload.FileName);
            s.TemplateContentType = AllowedFiles[ext];
        }
    }

    private static Dictionary<string, string> Snapshot(Service s, Dictionary<int, string> categories,
        Dictionary<int, string> institutions) => new()
    {
        ["Име"] = s.Name,
        ["Описание"] = s.Description,
        ["Категория"] = categories.GetValueOrDefault(s.CategoryId, ""),
        ["Институция"] = institutions.GetValueOrDefault(s.InstitutionId, ""),
        ["Документи"] = s.RequiredDocuments,
        ["Служебно"] = s.OfficialDocuments,
        ["Стъпки"] = s.Steps,
        ["Уточняващ въпрос"] = s.Question,
        ["Отговори"] = s.QuestionOptions,
        ["Адрес"] = s.Address,
        ["Търсене в картата"] = s.PlaceSearch,
        ["Работно време"] = s.WorkingHours,
        ["Телефон"] = s.Phone,
        ["Такса"] = s.Fee.ToString("0.00"),
        ["Такса бърза"] = s.ExpressFee?.ToString("0.00") ?? "",
        ["Срок"] = s.DeadlineDays.ToString(),
        ["Срок бърза"] = s.ExpressDeadlineDays?.ToString() ?? "",
        ["Получаване"] = s.ResultDelivery,
        ["Ключови думи"] = s.Keywords,
        ["Образец"] = s.TemplateFileName ?? "",
        ["Онлайн заявление"] = s.AllowOnline ? "да" : "не"
    };

    private static string Short(string text) => text.Length > 60 ? text[..57] + "..." : text;

    private async Task FillListsAsync(int? categoryId, int? institutionId)
    {
        ViewBag.Categories = new SelectList(await _db.Categories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", categoryId);
        ViewBag.Institutions = new SelectList(await _db.Institutions.OrderBy(i => i.Name).ToListAsync(), "Id", "Name", institutionId);
    }
}
