using Microsoft.AspNetCore.Authentication.Cookies;

namespace DigitalAssistant.Services;

// Бисквитките за вход имат различно име за http и за https.
// Браузърът не различава портовете, затова бисквитка от https://localhost:7053 се вижда и от http://localhost:5295.
// Някои браузъри не позволяват http страница да замени „сигурна“ бисквитка от https и тогава входът не се запазва.
// С отделни имена двата адреса не си пречат.
public class SchemeCookieManager : ICookieManager
{
    private readonly ChunkingCookieManager _inner = new();

    private static string Name(HttpContext context, string key) => context.Request.IsHttps ? key : key + ".Http";

    public string? GetRequestCookie(HttpContext context, string key) => _inner.GetRequestCookie(context, Name(context, key));

    public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options) =>
        _inner.AppendResponseCookie(context, Name(context, key), value, options);

    public void DeleteCookie(HttpContext context, string key, CookieOptions options) =>
        _inner.DeleteCookie(context, Name(context, key), options);
}
