using System.Security.Claims;
using DigitalAssistant.Data;
using DigitalAssistant.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DigitalAssistant.Services;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Вход и изход на администратора (т. 3.1.5 и 3.2.5)
[Area("Admin")]
public class AccountController : Controller
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockTime = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly PasswordHasher<AdminUser> _hasher = new();

    public AccountController(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        return View(new LoginModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // Потребителските имена се пазят с малки букви, затова „Admin“ и „admin“ са едно и също
        var username = model.Username.Trim().ToLowerInvariant();
        var admin = await _db.AdminUsers.FirstOrDefaultAsync(a => a.Username == username && a.IsActive);
        if (admin == null)
        {
            ModelState.AddModelError("", UnknownLogins.Fail(_cache, "admin:" + username, MaxFailedAttempts, LockTime, "Грешно потребителско име или парола."));
            return View(model);
        }

        if (admin.LockedUntil != null && admin.LockedUntil > DateTime.Now)
        {
            var minutes = (int)Math.Ceiling((admin.LockedUntil.Value - DateTime.Now).TotalMinutes);
            ModelState.AddModelError("", $"Профилът е заключен след {MaxFailedAttempts} грешни опита. Опитайте отново след {minutes} мин.");
            return View(model);
        }

        if (_hasher.VerifyHashedPassword(admin, admin.PasswordHash, model.Password) == PasswordVerificationResult.Failed)
        {
            admin.FailedAttempts++;
            if (admin.FailedAttempts >= MaxFailedAttempts)
            {
                admin.LockedUntil = DateTime.Now.Add(LockTime);
                admin.FailedAttempts = 0;
                await _db.SaveChangesAsync();
                ModelState.AddModelError("", $"Профилът е заключен за {LockTime.TotalMinutes} минути след {MaxFailedAttempts} грешни опита.");
                return View(model);
            }
            await _db.SaveChangesAsync();
            ModelState.AddModelError("", $"Грешно потребителско име или парола. {UnknownLogins.AttemptsLeft(MaxFailedAttempts - admin.FailedAttempts)}");
            return View(model);
        }

        admin.FailedAttempts = 0;
        admin.LockedUntil = null;
        admin.LastLoginAt = DateTime.Now;
        AuditService.Log(_db, admin.Username, "Вход", $"Вход от {HttpContext.Connection.RemoteIpAddress}");
        await _db.SaveChangesAsync();

        await SignInAsync(admin);

        if (admin.MustChangePassword)
        {
            TempData["Info"] = "Това е първият ви вход или паролата е временна. Изберете своя нова парола.";
            return RedirectToAction(nameof(ChangePassword));
        }
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)) return Redirect(model.ReturnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Success"] = "Излязохте от администрацията.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordModel());

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var admin = await _db.AdminUsers.FirstAsync(a => a.Username == User.Identity!.Name);
        if (_hasher.VerifyHashedPassword(admin, admin.PasswordHash, model.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Сегашната парола е грешна.");
            return View(model);
        }

        if (model.NewPassword == model.CurrentPassword)
        {
            ModelState.AddModelError(nameof(model.NewPassword), "Новата парола трябва да е различна от сегашната.");
            return View(model);
        }

        admin.PasswordHash = _hasher.HashPassword(admin, model.NewPassword);
        admin.MustChangePassword = false;
        AuditService.Log(_db, admin.Username, "Сменена парола", "Служителят смени паролата си.");
        await _db.SaveChangesAsync();
        // Входът тук продължава с новата парола, а на другите компютри спира
        await SignInAsync(admin);
        TempData["Success"] = "Паролата е сменена. Ако сте влезли и от друг компютър, там ще трябва да влезете отново.";
        return RedirectToAction("Index", "Dashboard");
    }

    private Task SignInAsync(AdminUser admin)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, admin.Username), CitizenAuth.StampFor(admin.PasswordHash) },
            CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }
}
