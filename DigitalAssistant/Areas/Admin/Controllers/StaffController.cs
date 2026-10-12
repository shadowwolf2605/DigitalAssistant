using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Служители (администратори). Само главният администратор добавя, деактивира и сменя пароли на служители.
public class StaffController : AdminController
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<AdminUser> _hasher = new();

    public StaffController(AppDbContext db) => _db = db;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!await _db.AdminUsers.AnyAsync(a => a.Username == AdminName && a.IsMain))
        {
            context.Result = NotFound();
            return;
        }
        await base.OnActionExecutionAsync(context, next);
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Form = new StaffFormModel();
        return await ListAsync();
    }

    private async Task<IActionResult> ListAsync()
    {
        ViewBag.Institutions = await _db.Institutions.OrderBy(i => i.Name).ToListAsync();
        return View(nameof(Index), await _db.AdminUsers.Include(a => a.Institution)
            .OrderByDescending(a => a.IsMain).ThenBy(a => a.FullName).ToListAsync());
    }

    // Институцията на служителя: служебните справки до нея се показват на него
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetInstitution(int id, int? institutionId)
    {
        var admin = await _db.AdminUsers.FindAsync(id);
        if (admin == null) return NotFound();
        var institution = institutionId == null ? null : await _db.Institutions.FindAsync(institutionId);
        if (institutionId != null && institution == null) return NotFound();
        if (admin.IsMain && institution != null)
        {
            TempData["Error"] = "Главният администратор е към центъра и не може да е към друга институция.";
            return RedirectToAction(nameof(Index));
        }
        admin.InstitutionId = institution?.Id;
        AuditService.Log(_db, AdminName, "Сменена институция на служител",
            $"{admin.FullName} ({admin.Username}): {institution?.Name ?? "центърът (всички институции)"}");
        await _db.SaveChangesAsync();
        Success($"{admin.FullName} вече е към {institution?.Name ?? "центъра и вижда справките до всички институции"}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] StaffFormModel model)
    {
        var username = (model.Username ?? "").Trim().ToLowerInvariant();
        if (ModelState.IsValid && await _db.AdminUsers.AnyAsync(a => a.Username == username))
            ModelState.AddModelError("Form.Username", "Вече има служител с това потребителско име.");
        if (!ModelState.IsValid)
        {
            ViewBag.Form = model;
            return await ListAsync();
        }

        var admin = new AdminUser
        {
            Username = username, FullName = model.FullName.Trim(), MustChangePassword = true,
            InstitutionId = model.InstitutionId != null && await _db.Institutions.AnyAsync(i => i.Id == model.InstitutionId) ? model.InstitutionId : null
        };
        admin.PasswordHash = _hasher.HashPassword(admin, model.Password);
        _db.AdminUsers.Add(admin);
        AuditService.Log(_db, AdminName, "Добавен служител", $"{admin.FullName} ({admin.Username})");
        await _db.SaveChangesAsync();
        Success($"Служителят {admin.FullName} е добавен. При първия вход ще трябва да смени временната парола.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var admin = await _db.AdminUsers.FindAsync(id);
        if (admin == null || admin.IsMain) return NotFound();
        admin.IsActive = !admin.IsActive;
        AuditService.Log(_db, AdminName, admin.IsActive ? "Активиран служител" : "Деактивиран служител", $"{admin.FullName} ({admin.Username})");
        await _db.SaveChangesAsync();
        Success(admin.IsActive ? $"{admin.FullName} отново може да влиза." : $"{admin.FullName} вече не може да влиза в системата.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, string? password)
    {
        var admin = await _db.AdminUsers.FindAsync(id);
        if (admin == null || admin.IsMain) return NotFound();
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            TempData["Error"] = "Временната парола трябва да е поне 8 знака.";
            return RedirectToAction(nameof(Index));
        }
        admin.PasswordHash = _hasher.HashPassword(admin, password);
        admin.MustChangePassword = true;
        admin.FailedAttempts = 0;
        admin.LockedUntil = null;
        AuditService.Log(_db, AdminName, "Нова временна парола", $"{admin.FullName} ({admin.Username})");
        await _db.SaveChangesAsync();
        Success($"{admin.FullName} има нова временна парола. При вход ще трябва да я смени.");
        return RedirectToAction(nameof(Index));
    }
}
