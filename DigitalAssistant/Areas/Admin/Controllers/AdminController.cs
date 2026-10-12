using DigitalAssistant.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Areas.Admin.Controllers;

// Общ клас за страниците на администратора. Достъпът е само след вход.
// Служител с временна парола първо трябва да я смени.
// Служител на друга институция (с избрана институция) вижда само служебните справки до нея (т. 3.2.5).
[Area("Admin")]
[Authorize]
public abstract class AdminController : Controller
{
    protected string AdminName => User.Identity?.Name ?? "";

    protected void Success(string message) => TempData["Success"] = message;

    // Страниците, които може да отваря и служител на друга институция
    protected virtual bool ForInstitutionStaff => false;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var db = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var me = await db.AdminUsers.AsNoTracking().Where(a => a.Username == AdminName)
            .Select(a => new { a.MustChangePassword, a.InstitutionId }).FirstOrDefaultAsync();
        if (me?.MustChangePassword == true)
        {
            context.Result = RedirectToAction("ChangePassword", "Account", new { area = "Admin" });
            return;
        }
        if (me?.InstitutionId != null && !ForInstitutionStaff)
        {
            context.Result = RedirectToAction("Index", "InfoRequests", new { area = "Admin" });
            return;
        }
        await next();
    }
}

// Ред в списъка с категории или институции
public record ItemRow(int Id, string Name, int ActiveServices, int AllServices);
