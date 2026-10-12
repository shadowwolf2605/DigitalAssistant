using System.Security.Claims;
using DigitalAssistant.Data;
using DigitalAssistant.Models;
using DigitalAssistant.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigitalAssistant.Controllers;

// Регистрация, вход и изход на гражданите (т. 3.1.3 и 3.2.5)
public class AccountController : Controller
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockTime = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly PersonalDataProtector _protector;
    private readonly Notifications _notifications;
    private readonly PasswordHasher<Citizen> _hasher = new();
    private readonly IMemoryCache _cache;

    private readonly EmailService _email;

    public AccountController(AppDbContext db, PersonalDataProtector protector, Notifications notifications, IMemoryCache cache, EmailService email)
    {
        _email = email;
        _db = db;
        _protector = protector;
        _notifications = notifications;
        _cache = cache;
    }

    // Грешни опити за имейл, за който няма профил. Пазят се само в паметта за 15 минути,
    // за да получи човек същите съобщения като при съществуващ профил и да не може да разбере кои имейли са регистрирани.
    [HttpGet]
    public IActionResult Register(string? returnUrl = null) => View(new RegisterModel { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Register(RegisterModel model)
    {
        var email = (model.Email ?? "").Trim().ToLowerInvariant();
        if (!model.Consent)
            ModelState.AddModelError(nameof(model.Consent), "Трябва да се съгласите личните ви данни да се използват за заявленията.");
        if (!string.IsNullOrWhiteSpace(model.Egn) && Egn.Validate(model.Egn) is string egnError)
            ModelState.AddModelError(nameof(model.Egn), egnError);
        if (ModelState.IsValid && await _db.Citizens.AnyAsync(c => c.Email == email))
            ModelState.AddModelError(nameof(model.Email), "Вече има профил с този имейл.");
        var egnHash = ModelState.IsValid ? _protector.Hash(model.Egn) : "";
        if (ModelState.IsValid && await _db.Citizens.AnyAsync(c => c.EgnHash == egnHash))
            ModelState.AddModelError(nameof(model.Egn), "Вече има профил с това ЕГН. Ако сте забравили паролата си, използвайте „Забравена парола“.");
        if (!ModelState.IsValid) return View(model);

        var citizen = new Citizen
        {
            FullName = model.FullName.Trim(), Email = email, Phone = model.Phone.Trim(),
            EgnProtected = _protector.Protect(model.Egn), EgnHash = egnHash
        };
        citizen.PasswordHash = _hasher.HashPassword(citizen, model.Password);
        // Без пощенски сървър (пробна версия в интернет) писмото няма как да стигне, затова имейлът се приема за потвърден
        citizen.EmailConfirmed = !_email.CanDeliver;
        _db.Citizens.Add(citizen);
        await _db.SaveChangesAsync();
        if (_email.CanDeliver) await _notifications.SendEmailConfirmationAsync(citizen);

        await SignInAsync(citizen);
        TempData["Success"] = _email.CanDeliver
            ? $"Профилът е създаден. Изпратихме писмо на {email}. Отворете връзката в него, за да потвърдите имейла си и да можете да подавате заявления."
            : "Профилът е създаден. Можете да подавате заявления.";
        return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/Applications");
    }

    // Връзката от писмото за потвърждение на имейла
    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string? token)
    {
        var entry = await Notifications.FindTokenAsync(_db, token, CitizenToken.ConfirmEmail);
        if (entry == null)
        {
            // Връзката е отворена втори път, а имейлът вече е потвърден – това не е грешка
            var hash = Notifications.HashToken(token ?? "");
            ViewBag.Ok = !string.IsNullOrWhiteSpace(token) && await _db.CitizenTokens.AnyAsync(t =>
                t.TokenHash == hash && t.Purpose == CitizenToken.ConfirmEmail && t.UsedAt != null && t.Citizen!.EmailConfirmed);
            return View();
        }
        entry.UsedAt = DateTime.Now;
        entry.Citizen!.EmailConfirmed = true;
        await _db.SaveChangesAsync();
        ViewBag.Ok = true;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    [Authorize(AuthenticationSchemes = CitizenAuth.Scheme)]
    public async Task<IActionResult> ResendConfirmation(string? returnUrl)
    {
        var citizen = await _db.Citizens.FirstAsync(c => c.Id == CitizenAuth.Id(User));
        if (!citizen.EmailConfirmed)
        {
            await _notifications.SendEmailConfirmationAsync(citizen);
            TempData["Success"] = $"Изпратихме ново писмо на {citizen.Email}.";
        }
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Applications");
    }

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        ViewBag.NoEmail = !_email.CanDeliver;
        return View(new ForgotPasswordModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordModel model)
    {
        ViewBag.NoEmail = !_email.CanDeliver;
        if (!ModelState.IsValid || !_email.CanDeliver) return View(model);
        var email = model.Email.Trim().ToLowerInvariant();
        var citizen = await _db.Citizens.FirstOrDefaultAsync(c => c.Email == email);
        if (citizen != null) await _notifications.SendPasswordResetAsync(citizen);
        // Съобщението е еднакво, за да не се разбира дали има профил с този имейл
        ViewBag.Sent = email;
        return View(new ForgotPasswordModel());
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string? token)
    {
        var entry = await Notifications.FindTokenAsync(_db, token, CitizenToken.ResetPassword);
        ViewBag.Valid = entry != null;
        return View(new ResetPasswordModel { Token = token ?? "" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPassword(ResetPasswordModel model)
    {
        var entry = await Notifications.FindTokenAsync(_db, model.Token, CitizenToken.ResetPassword);
        ViewBag.Valid = entry != null;
        // Ако връзката не важи, грешките за паролата нямат смисъл – показва се само, че връзката е изтекла
        if (entry == null) ModelState.Clear();
        if (entry == null || !ModelState.IsValid) return View(model);

        var citizen = entry.Citizen!;
        citizen.PasswordHash = _hasher.HashPassword(citizen, model.NewPassword);
        citizen.FailedAttempts = 0;
        citizen.LockedUntil = null;
        // Щом е получил писмото, имейлът му е потвърден
        citizen.EmailConfirmed = true;
        entry.UsedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Паролата е сменена. Влезте с новата парола.";
        return RedirectToAction(nameof(Login));
    }

    // Профил на гражданина
    [HttpGet]
    [Authorize(AuthenticationSchemes = CitizenAuth.Scheme)]
    public async Task<IActionResult> Profile()
    {
        var citizen = await _db.Citizens.FirstAsync(c => c.Id == CitizenAuth.Id(User));
        FillProfile(citizen);
        return View(new ProfileModel { Phone = citizen.Phone, EmailNotifications = citizen.EmailNotifications });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(AuthenticationSchemes = CitizenAuth.Scheme)]
    public async Task<IActionResult> Profile(ProfileModel model)
    {
        var citizen = await _db.Citizens.FirstAsync(c => c.Id == CitizenAuth.Id(User));
        if (!ModelState.IsValid)
        {
            FillProfile(citizen);
            return View(model);
        }
        citizen.Phone = model.Phone.Trim();
        citizen.EmailNotifications = model.EmailNotifications;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Данните са запазени.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = CitizenAuth.Scheme)]
    public IActionResult ChangePassword() => View(new ChangePasswordModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(AuthenticationSchemes = CitizenAuth.Scheme)]
    public async Task<IActionResult> ChangePassword(ChangePasswordModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var citizen = await _db.Citizens.FirstAsync(c => c.Id == CitizenAuth.Id(User));
        if (_hasher.VerifyHashedPassword(citizen, citizen.PasswordHash, model.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Сегашната парола е грешна.");
            return View(model);
        }
        if (model.NewPassword == model.CurrentPassword)
        {
            ModelState.AddModelError(nameof(model.NewPassword), "Новата парола трябва да е различна от сегашната.");
            return View(model);
        }
        citizen.PasswordHash = _hasher.HashPassword(citizen, model.NewPassword);
        await _db.SaveChangesAsync();
        // Входът на този компютър остава, а на всички други компютри спира да важи
        await SignInAsync(citizen);
        TempData["Success"] = "Паролата е сменена. Ако сте влезли и от друг компютър, там ще трябва да влезете отново.";
        return RedirectToAction(nameof(Profile));
    }

    private void FillProfile(Citizen citizen)
    {
        ViewBag.Citizen = citizen;
        ViewBag.EgnMasked = Egn.Mask(_protector.Unprotect(citizen.EgnProtected));
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new CitizenLoginModel { ReturnUrl = returnUrl });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(CitizenLoginModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var email = model.Email.Trim().ToLowerInvariant();
        var citizen = await _db.Citizens.FirstOrDefaultAsync(c => c.Email == email);
        if (citizen == null)
        {
            ModelState.AddModelError("", UnknownLogins.Fail(_cache, email, MaxFailedAttempts, LockTime, "Грешен имейл или парола."));
            return View(model);
        }
        if (citizen.LockedUntil != null && citizen.LockedUntil > DateTime.Now)
        {
            var minutes = (int)Math.Ceiling((citizen.LockedUntil.Value - DateTime.Now).TotalMinutes);
            ModelState.AddModelError("", $"Профилът е заключен след {MaxFailedAttempts} грешни опита. Опитайте отново след {minutes} мин.");
            return View(model);
        }
        if (_hasher.VerifyHashedPassword(citizen, citizen.PasswordHash, model.Password) == PasswordVerificationResult.Failed)
        {
            citizen.FailedAttempts++;
            if (citizen.FailedAttempts >= MaxFailedAttempts)
            {
                citizen.LockedUntil = DateTime.Now.Add(LockTime);
                citizen.FailedAttempts = 0;
                await _db.SaveChangesAsync();
                ModelState.AddModelError("", $"Профилът е заключен за {LockTime.TotalMinutes} минути след {MaxFailedAttempts} грешни опита.");
                return View(model);
            }
            await _db.SaveChangesAsync();
            ModelState.AddModelError("", $"Грешен имейл или парола. {UnknownLogins.AttemptsLeft(MaxFailedAttempts - citizen.FailedAttempts)}");
            return View(model);
        }

        citizen.FailedAttempts = 0;
        citizen.LockedUntil = null;
        await _db.SaveChangesAsync();
        await SignInAsync(citizen);
        return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/Applications");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CitizenAuth.Scheme);
        return RedirectToAction("Index", "Home");
    }

    private Task SignInAsync(Citizen c)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, c.Id.ToString()),
            new Claim(ClaimTypes.Name, c.FullName),
            new Claim(ClaimTypes.Email, c.Email),
            CitizenAuth.StampFor(c.PasswordHash)
        }, CitizenAuth.Scheme);
        return HttpContext.SignInAsync(CitizenAuth.Scheme, new ClaimsPrincipal(identity));
    }
}
