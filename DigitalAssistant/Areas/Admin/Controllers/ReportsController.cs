using DigitalAssistant.Data;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Справки с изтегляне като .xls и .pdf (т. 3.1.4)
public class ReportsController : AdminController
{
    private readonly ReportService _reports;
    private readonly ExportService _export;
    private readonly AppDbContext _db;

    public ReportsController(ReportService reports, ExportService export, AppDbContext db)
    {
        _reports = reports;
        _export = export;
        _db = db;
    }

    public async Task<IActionResult> Index(ReportRequest request, bool show = false)
    {
        var error = CheckDates(request);
        if (error != null) ModelState.AddModelError("", error);
        if (!ModelState.IsValid) show = false;

        ViewBag.Request = request;
        ViewBag.Categories = new SelectList(await _db.Categories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", request.CategoryId);
        ViewBag.Institutions = new SelectList(await _db.Institutions.OrderBy(i => i.Name).ToListAsync(), "Id", "Name", request.InstitutionId);
        ReportTable? table = show ? await _reports.BuildAsync(request) : null;
        return View(table);
    }

    private string? CheckDates(ReportRequest request)
    {
        // Празна или грешна дата: съобщението казва коя е
        foreach (var (key, name) in new[] { (nameof(request.From), "начална"), (nameof(request.To), "крайна") })
        {
            if (ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0)
            {
                ModelState.Remove(key);
                ModelState.AddModelError("", $"Изберете {name} дата.");
            }
        }
        if (!ModelState.IsValid) return null;
        if (request.From.Year < 2000 || request.To.Year > 2100) return "Изберете дати между 2000 и 2100 г.";
        if (request.From > request.To) return "Началната дата трябва да е преди крайната.";
        return null;
    }

    public async Task<IActionResult> Export(ReportRequest request, string format)
    {
        if (!ModelState.IsValid || CheckDates(request) != null) return RedirectToAction(nameof(Index), request);

        var table = await _reports.BuildAsync(request);
        var name = $"spravka-{request.Type}-{DateTime.Now:yyyy-MM-dd}";
        return format == "pdf"
            ? File(_export.ToPdf(table), "application/pdf", name + ".pdf")
            : File(_export.ToXls(table), "application/vnd.ms-excel", name + ".xls");
    }
}
