using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace DigitalAssistant.Services;

// Настройки на администраторската част (раздел "AdminSite" в appsettings.json)
public class AdminSiteOptions
{
    // Портове, на които работи администраторската част. Сайтът за гражданите е на другите портове.
    public int[] Ports { get; set; } = { 5295, 7053 };

    // Мрежи, от които може да се отваря администраторската част (вътрешната мрежа на центъра)
    public string[] AllowedNetworks { get; set; } = { "127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" };

    // Адресът на сайта за гражданите, напр. https://uslugi.example.bg.
    // Ако е празен, се взима от адресите, на които работи приложението.
    public string? PublicUrl { get; set; }

    // true – администрацията е на същия адрес като сайта, под /Admin (когато сайтът е качен в интернет
    // и има само един адрес). Мрежите от AllowedNetworks пак важат.
    public bool SameAddress { get; set; }

    // Началната парола на главния администратор. Ако е празна, е Admin123!.
    // При качване в интернет се задава друга, за да не може всеки да влезе с известната парола.
    public string? InitialPassword { get; set; }
}

// Администраторите влизат от отделен адрес, който не е част от сайта за гражданите (т. 3.1.5).
// - на адреса за гражданите /Admin връща „Страницата не е намерена“;
// - на адреса за администраторите се отварят само страниците на администрацията;
// - администрацията се отваря само от разрешените мрежи.
public class AdminSiteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AdminSiteOptions _options;
    private readonly List<IPNetwork> _networks;

    public AdminSiteMiddleware(RequestDelegate next, AdminSiteOptions options)
    {
        _next = next;
        _options = options;
        _networks = options.AllowedNetworks.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => IPNetwork.Parse(n.Trim())).ToList();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        bool adminPath = path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase);
        bool adminPort = _options.SameAddress ? adminPath : _options.Ports.Contains(context.Connection.LocalPort);

        if (adminPath && (!adminPort || !IsAllowed(context)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Страниците за грешки се показват и на двата адреса
        bool errorPage = path.StartsWithSegments("/Home/StatusCode", StringComparison.OrdinalIgnoreCase)
                         || path.StartsWithSegments("/Home/Error", StringComparison.OrdinalIgnoreCase);
        if (adminPort && !adminPath && !errorPage)
        {
            context.Response.Redirect("/Admin");
            return;
        }

        await _next(context);
    }

    // Посредникът на хостинга е разрешен (ASPNETCORE_FORWARDEDHEADERS_ENABLED=true) и истинският адрес на човека вече е взет от него
    private static readonly bool TrustedProxy =
        string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

    private bool IsAllowed(HttpContext context)
    {
        // Заявка, минала през чужд посредник (например тунел до компютъра), изглежда като от самия компютър.
        // Затова, ако посредникът не е разрешен в настройките (ForwardedHeaders), администрацията не се отваря.
        if (!TrustedProxy && (context.Request.Headers.ContainsKey("X-Forwarded-For") || context.Request.Headers.ContainsKey("X-Forwarded-Host")))
            return false;
        var ip = context.Connection.RemoteIpAddress;
        if (ip == null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return _networks.Any(n => n.Contains(ip));
    }
}

// Пренасочва от http към https: сайта за гражданите към неговия https адрес, а администрацията – към нейния.
// Вграденото пренасочване не работи, когато има два https адреса. Ако няма https адрес (профилът http), не пренасочва.
public class HttpsRedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AdminSiteOptions _options;
    private readonly IServer _server;
    private (int? Public, int? Admin)? _httpsPorts;

    public HttpsRedirectMiddleware(RequestDelegate next, AdminSiteOptions options, IServer server)
    {
        _next = next;
        _options = options;
        _server = server;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.IsHttps)
        {
            var ports = _httpsPorts ??= FindHttpsPorts();
            var port = !_options.SameAddress && _options.Ports.Contains(context.Connection.LocalPort) ? ports.Admin : ports.Public;
            if (port != null)
            {
                var request = context.Request;
                var url = $"https://{request.Host.Host}:{port}{request.PathBase}{request.Path}{request.QueryString}";
                context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
                context.Response.Headers.Location = url;
                return;
            }
        }
        await _next(context);
    }

    private (int? Public, int? Admin) FindHttpsPorts()
    {
        int? publicPort = null, adminPort = null;
        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses ?? Array.Empty<string>();
        foreach (var address in addresses)
        {
            var match = System.Text.RegularExpressions.Regex.Match(address, @"^https://.+:(\d+)/?$");
            if (!match.Success) continue;
            var port = int.Parse(match.Groups[1].Value);
            if (_options.Ports.Contains(port)) adminPort ??= port;
            else publicPort ??= port;
        }
        return (publicPort, adminPort);
    }
}

// Адресът на сайта за гражданите. Нужен е за връзките в имейлите и в менюто на администратора.
public class SiteUrls
{
    private readonly AdminSiteOptions _options;
    private readonly IServer _server;

    public SiteUrls(AdminSiteOptions options, IServer server)
    {
        _options = options;
        _server = server;
    }

    public string PublicBase
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_options.PublicUrl)) return _options.PublicUrl.TrimEnd('/');
            // Адресът, който дава Render, когато сайтът е качен там
            var hosted = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL");
            if (!string.IsNullOrWhiteSpace(hosted)) return hosted.TrimEnd('/');
            var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses ?? Array.Empty<string>();
            foreach (var address in addresses)
            {
                if (!Uri.TryCreate(address.Replace("://*", "://localhost").Replace("://+", "://localhost")
                        .Replace("://[::]", "://localhost").Replace("://0.0.0.0", "://localhost"), UriKind.Absolute, out var uri))
                    continue;
                if (!_options.Ports.Contains(uri.Port)) return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            }
            return "http://localhost:5294";
        }
    }

    public string Public(string path) => PublicBase + (path.StartsWith('/') ? path : "/" + path);
}
