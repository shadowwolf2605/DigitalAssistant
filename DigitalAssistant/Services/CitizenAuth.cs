using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DigitalAssistant.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace DigitalAssistant.Services;

// Вход на гражданите е отделен от входа на администраторите (друга бисквитка)
public static class CitizenAuth
{
    public const string Scheme = "Citizen";

    // Схема по подразбиране: страниците под /Admin ползват входа на администраторите, всички други – на гражданите
    public const string AutoScheme = "Auto";

    private const string StampClaim = "stamp";

    public static int Id(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    // Отпечатък на паролата. Когато паролата се смени, старите влизания (напр. на друг компютър) спират да важат.
    // Ползва се и за администраторите (AdminSession).
    public static string Stamp(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..16];

    public static Claim StampFor(string passwordHash) => new(StampClaim, Stamp(passwordHash));

    public static bool HasStamp(ClaimsPrincipal user, string passwordHash) => user.FindFirstValue(StampClaim) == Stamp(passwordHash);

    // При всяка заявка се проверява, че профилът съществува и паролата не е сменяна след влизането
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var id = context.Principal == null ? 0 : Id(context.Principal);
        var hash = await db.Citizens.Where(c => c.Id == id).Select(c => c.PasswordHash).FirstOrDefaultAsync();
        if (hash == null || !HasStamp(context.Principal!, hash))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
        }
    }
}
