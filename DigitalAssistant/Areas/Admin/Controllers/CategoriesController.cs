using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Категориите, от които се избира във формуляра за услуга (т. 3.1.1)
public class CategoriesController : AdminController
{
    private readonly AppDbContext _db;

    public CategoriesController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        ViewBag.Items = await _db.Categories
            .OrderBy(x => x.Name)
            .Select(x => new ItemRow(x.Id, x.Name, x.Services.Count(s => s.IsActive), x.Services.Count))
            .ToListAsync();
        return View("~/Areas/Admin/Views/Shared/NamedList.cshtml", new Category());
    }

    // Адресът остава в браузъра след грешка във формуляра – при отваряне се връща към списъка
    [HttpGet]
    public IActionResult Create() => RedirectToAction(nameof(Index));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category model)
    {
        model.Name = model.Name?.Trim() ?? "";
        await CheckUniqueAsync(model);
        if (!ModelState.IsValid) return await Index(model);

        _db.Categories.Add(model);
        AuditService.Log(_db, AdminName, "Добавена категория", model.Name);
        await _db.SaveChangesAsync();
        Success($"„{model.Name}“ е добавена.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var item = await _db.Categories.FindAsync(id);
        if (item == null) return NotFound();
        return View("~/Areas/Admin/Views/Shared/NamedEdit.cshtml", item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Category model)
    {
        var item = await _db.Categories.FindAsync(id);
        if (item == null) return NotFound();
        model.Id = id;
        model.Name = model.Name?.Trim() ?? "";
        await CheckUniqueAsync(model);
        if (!ModelState.IsValid) return View("~/Areas/Admin/Views/Shared/NamedEdit.cshtml", model);

        if (item.Name != model.Name)
        {
            AuditService.Log(_db, AdminName, "Променена категория", $"„{item.Name}“ → „{model.Name}“");
            item.Name = model.Name;
            await _db.SaveChangesAsync();
        }
        Success("Промените са запазени.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> Index(Category model)
    {
        ViewBag.Items = await _db.Categories
            .OrderBy(x => x.Name)
            .Select(x => new ItemRow(x.Id, x.Name, x.Services.Count(s => s.IsActive), x.Services.Count))
            .ToListAsync();
        return View("~/Areas/Admin/Views/Shared/NamedList.cshtml", model);
    }

    private async Task CheckUniqueAsync(Category model)
    {
        var names = await _db.Categories.Where(x => x.Id != model.Id).Select(x => x.Name).ToListAsync();
        if (names.Any(n => string.Equals(n, model.Name, StringComparison.OrdinalIgnoreCase)))
            ModelState.AddModelError(nameof(model.Name), "Вече има категория с това име.");
    }
}
