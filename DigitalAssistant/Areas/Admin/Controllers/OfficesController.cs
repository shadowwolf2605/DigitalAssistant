using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Адресите на институциите по градове (т. 3.1.1). Помощникът показва адреса и телефона в града на човека.
// Служител с избран град добавя и променя само офисите в своя град.
public class OfficesController : AdminController
{
    private readonly AppDbContext _db;

    public OfficesController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? city, int? institutionId)
    {
        ViewBag.Form = new Office { City = AdminCity ?? (GuideCities.All.Any(c => c.Name == city) ? city! : ""), InstitutionId = institutionId ?? 0 };
        return await ListAsync(city, institutionId);
    }

    private async Task<IActionResult> ListAsync(string? city, int? institutionId)
    {
        if (AdminCity != null) city = AdminCity;
        var query = _db.Offices.Include(o => o.Institution).AsQueryable();
        if (!string.IsNullOrEmpty(city)) query = query.Where(o => o.City == city);
        if (institutionId != null) query = query.Where(o => o.InstitutionId == institutionId);
        ViewBag.City = city;
        ViewBag.InstitutionId = institutionId;
        ViewBag.MyCity = AdminCity;
        ViewBag.Institutions = await _db.Institutions.OrderBy(i => i.Name).ToListAsync();
        ViewBag.CitiesWithOffices = await _db.Offices.Select(o => o.City).Distinct().CountAsync();
        return View(nameof(Index), await query.OrderBy(o => o.City).ThenBy(o => o.Institution!.Name).ToListAsync());
    }

    [HttpGet]
    public IActionResult Create() => RedirectToAction(nameof(Index));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] Office model)
    {
        await CheckAsync(model, "Form.");
        if (!ModelState.IsValid)
        {
            ViewBag.Form = model;
            return await ListAsync(null, null);
        }
        _db.Offices.Add(model);
        var institution = await _db.Institutions.Where(i => i.Id == model.InstitutionId).Select(i => i.Name).FirstAsync();
        AuditService.Log(_db, AdminName, "Добавен офис", $"{institution} – {model.City}: {model.Address}");
        await _db.SaveChangesAsync();
        Success($"Офисът на „{institution}“ в {model.City} е добавен. Помощникът вече го показва на хората от този град.");
        return RedirectToAction(nameof(Index), new { city = model.City });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var office = await _db.Offices.Include(o => o.Institution).FirstOrDefaultAsync(o => o.Id == id);
        if (office == null || !Mine(office)) return NotFound();
        ViewBag.Institutions = await _db.Institutions.OrderBy(i => i.Name).ToListAsync();
        ViewBag.MyCity = AdminCity;
        return View(office);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Office model)
    {
        var office = await _db.Offices.FindAsync(id);
        if (office == null || !Mine(office)) return NotFound();
        model.Id = id;
        await CheckAsync(model);
        if (!ModelState.IsValid)
        {
            ViewBag.Institutions = await _db.Institutions.OrderBy(i => i.Name).ToListAsync();
            ViewBag.MyCity = AdminCity;
            return View(model);
        }
        office.InstitutionId = model.InstitutionId;
        office.City = model.City;
        office.Address = model.Address;
        office.Phone = model.Phone;
        office.WorkingHours = model.WorkingHours;
        AuditService.Log(_db, AdminName, "Променен офис", $"{office.City}: {office.Address}");
        await _db.SaveChangesAsync();
        Success("Промените са запазени.");
        return RedirectToAction(nameof(Index), new { city = office.City });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var office = await _db.Offices.Include(o => o.Institution).FirstOrDefaultAsync(o => o.Id == id);
        if (office == null || !Mine(office)) return NotFound();
        _db.Offices.Remove(office);
        AuditService.Log(_db, AdminName, "Изтрит офис", $"{office.Institution?.Name} – {office.City}: {office.Address}");
        await _db.SaveChangesAsync();
        Success($"Офисът в {office.City} е изтрит. Там помощникът отново ще показва търсене в картата.");
        return RedirectToAction(nameof(Index), new { city = office.City });
    }

    private bool Mine(Office office) => AdminCity == null || office.City == AdminCity;

    private async Task CheckAsync(Office model, string prefix = "")
    {
        model.Address = (model.Address ?? "").Trim();
        model.Phone = (model.Phone ?? "").Trim();
        model.WorkingHours = (model.WorkingHours ?? "").Trim();
        model.City = (model.City ?? "").Trim();
        // Телефонът и работното време не са задължителни
        foreach (var key in new[] { "Phone", "WorkingHours", "Institution" })
            ModelState.Remove(prefix + key);
        // Градът е от списъка с областните градове, за да съвпада с града, който помощникът определя
        if (!GuideCities.All.Any(c => c.Name == model.City))
            ModelState.AddModelError(prefix + "City", "Изберете град от списъка.");
        else if (AdminCity != null && model.City != AdminCity)
            ModelState.AddModelError(prefix + "City", $"Можете да добавяте офиси само в {AdminCity}.");
        if (!await _db.Institutions.AnyAsync(i => i.Id == model.InstitutionId))
            ModelState.AddModelError(prefix + "InstitutionId", "Изберете институция.");
        else if (await _db.Offices.AnyAsync(o => o.Id != model.Id && o.InstitutionId == model.InstitutionId && o.City == model.City))
            ModelState.AddModelError(prefix + "City", "Тази институция вече има офис в този град. Променете него.");
    }
}
