namespace DigitalAssistant.Services;

// Заглавки за сигурност към всеки отговор на сървъра (т. 3.2.5):
// страниците не могат да се вграждат в чужди сайтове, а скриптове и стилове се зареждат само от този сайт.
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; " +
            "form-action 'self'; base-uri 'self'; object-src 'none'";
        return _next(context);
    }
}
