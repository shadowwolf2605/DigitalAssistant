using DigitalAssistant.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Services;

// При всяка заявка се проверява дали служителят все още е активен и дали паролата му не е сменена след входа.
// Ако главният администратор го е деактивирал или паролата е сменена (от него или от главния администратор),
// той излиза веднага – и на другите компютри.
public static class AdminSession
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var name = context.Principal?.Identity?.Name;
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var hash = name == null ? null
            : await db.AdminUsers.Where(a => a.Username == name && a.IsActive).Select(a => a.PasswordHash).FirstOrDefaultAsync();
        if (hash == null || !CitizenAuth.HasStamp(context.Principal!, hash))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
