using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DigitalAssistant.Services;

// Ако входът изтече, докато човек попълва формуляр, след новия вход той трябва да се върне
// на страницата с формуляра, а не на адреса, към който формулярът се изпраща (там има само POST).
public static class LoginRedirect
{
    public static Func<RedirectContext<CookieAuthenticationOptions>, Task> For(Func<RedirectContext<CookieAuthenticationOptions>, Task> standard) =>
        context =>
        {
            var request = context.Request;
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                var returnUrl = "";
                if (Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
                    && string.Equals(referer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
                    && !referer.AbsolutePath.Contains("/Account/", StringComparison.OrdinalIgnoreCase))
                    returnUrl = referer.PathAndQuery;
                var login = context.Options.LoginPath.Value!;
                context.RedirectUri = returnUrl.Length > 0
                    ? login + QueryString.Create(context.Options.ReturnUrlParameter, returnUrl)
                    : login;
            }
            return standard(context);
        };
}
