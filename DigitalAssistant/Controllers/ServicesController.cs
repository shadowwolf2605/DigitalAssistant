using DigitalAssistant.Data;
using DigitalAssistant.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Controllers;

// Търсене и разглеждане на услугите от гражданите
public class ServicesController : Controller
{
    private readonly AppDbContext _db;

    public ServicesController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(ServiceFilterModel filter)
    {
        var query = _db.Services.Include(s => s.Category).Include(s => s.Institution)
            .Where(s => s.IsActive);
        if (filter.CategoryId != null) query = query.Where(s => s.CategoryId == filter.CategoryId);
        if (filter.InstitutionId != null) query = query.Where(s => s.InstitutionId == filter.InstitutionId);
        var services = await query.ToListAsync();

        // Търсенето по име е тук, защото SQLite не сравнява кирилица без значение от главни и малки букви
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            // Всяка дума трябва да я има в името или в ключовите думи, в какъвто и да е ред.
            // От дългите думи се сравнява само началото, за да се намира и „личната карта“.
            var words = filter.Name.Split(' ', ',', '.', '?', '!', ';').Select(w => w.Trim()).Where(w => w.Length > 1)
                .Select(w => w.Length > 5 ? w[..5] : w).ToList();
            if (words.Count > 0)
                services = services.Where(s => words.All(w => (s.Name + " " + s.Keywords).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        // В списъците са само категориите и институциите, които имат активни услуги
        ViewBag.Categories = new SelectList(await _db.Categories.Where(c => c.Services.Any(s => s.IsActive) || c.Id == filter.CategoryId)
            .OrderBy(c => c.Name).ToListAsync(), "Id", "Name", filter.CategoryId);
        ViewBag.Institutions = new SelectList(await _db.Institutions.Where(i => i.Services.Any(s => s.IsActive) || i.Id == filter.InstitutionId)
            .OrderBy(i => i.Name).ToListAsync(), "Id", "Name", filter.InstitutionId);
        ViewBag.Filter = filter;
        return View(services.OrderBy(s => s.Category!.Name).ThenBy(s => s.Name).ToList());
    }

    public async Task<IActionResult> Details(int id)
    {
        var service = await _db.Services.Include(s => s.Category).Include(s => s.Institution)
            .FirstOrDefaultAsync(s => s.Id == id && s.IsActive);
        if (service == null) return NotFound();
        ViewBag.Offices = await _db.Offices.AsNoTracking().Where(o => o.InstitutionId == service.InstitutionId)
            .OrderBy(o => o.City).ToListAsync();
        return View(service);
    }

    // Изтегляне на образеца на заявлението
    public async Task<IActionResult> Template(int id)
    {
        var service = await _db.Services.Include(s => s.Template).FirstOrDefaultAsync(s => s.Id == id && s.IsActive);
        if (service?.Template == null) return NotFound();
        return File(service.Template.File, service.TemplateContentType ?? "application/octet-stream", service.TemplateFileName);
    }
}
