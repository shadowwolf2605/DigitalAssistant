using Microsoft.AspNetCore.Authentication;
using System.Diagnostics;
using DigitalAssistant.Data;
using DigitalAssistant.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db) => _db = db;

    // Начална страница с прозореца за разговор с помощника
    public async Task<IActionResult> Index()
    {
        var categories = await _db.Categories
            .Select(c => new { c.Id, c.Name, Count = c.Services.Count(s => s.IsActive) })
            .OrderBy(c => c.Name)
            .ToListAsync();
        // Празните категории не се показват на гражданите
        ViewBag.Categories = categories.Where(c => c.Count > 0).Select(c => (c.Id, c.Name, c.Count)).ToList();
        ViewBag.ServiceCount = categories.Sum(c => c.Count);
        ViewBag.InstitutionCount = await _db.Institutions.CountAsync(i => i.Services.Any(s => s.IsActive));
        return View();
    }

    // Как се пазят и използват личните данни
    public IActionResult Privacy() => View();

    [Route("/Home/StatusCode/{code:int}")]
    public new async Task<IActionResult> StatusCode(int code)
    {
        ViewBag.Code = code;
        // Грешка в администрацията за влязъл администратор се показва в нейния изглед, с менюто
        var original = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>()?.OriginalPath;
        if (original != null && original.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase))
        {
            var admin = await HttpContext.AuthenticateAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            if (admin.Succeeded)
            {
                HttpContext.User = admin.Principal!;
                return View("AdminStatusCode");
            }
        }
        return View("StatusCode");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
