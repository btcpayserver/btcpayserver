#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Plugins.Translations;

public class LangCookieMiddleware(RequestDelegate next, LanguageService languageService, LocalizerService localizerService)
{
    // Stores the resolved Translations object for the current request.
    public const string ItemsKey = "btcpay_lang_translations";
    internal const string CookieName = "btcpay_lang";

    public async Task InvokeAsync(HttpContext ctx)
    {
        string? langCode = null;

        var queryLang = ctx.Request.Query["lang"].ToString();
        if (!string.IsNullOrEmpty(queryLang))
        {
            var found = languageService.FindLanguage(queryLang);
            if (found is not null)
            {
                langCode = found.Code;
                ctx.Response.Cookies.Append(CookieName, found.Code, new CookieOptions
                {
                    MaxAge = TimeSpan.FromDays(365),
                    HttpOnly = true,
                    Secure = ctx.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true
                });
            }
        }
        else if (ctx.Request.Cookies.TryGetValue(CookieName, out var cookieValue) &&
                 !string.IsNullOrEmpty(cookieValue))
        {
            // Validate cookie value to avoid DB lookups for arbitrary/tampered values.
            var found = languageService.FindLanguage(cookieValue);
            if (found is not null)
                langCode = found.Code;
        }

        if (langCode is not null)
        {
            // Resolve and cache the Translations object here (async context) so the
            // synchronous IStringLocalizer hot path can read it directly from HttpContext.Items.
            var translations = await localizerService.GetOrLoadForLanguageCode(langCode);
            ctx.Items[ItemsKey] = translations;
        }

        await next(ctx);
    }
}

public class LangCookieStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseMiddleware<LangCookieMiddleware>();
            next(app);
        };
}
