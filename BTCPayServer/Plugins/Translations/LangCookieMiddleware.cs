#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Plugins.Translations;

public class LangCookieMiddleware(RequestDelegate next, LanguageService languageService)
{
    public const string ItemsKey = "btcpay_lang_dict";
    internal const string CookieName = "btcpay_lang";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var queryLang = ctx.Request.Query["lang"].ToString();
        string? dictName = null;

        if (!string.IsNullOrEmpty(queryLang))
        {
            var found = languageService.FindLanguage(queryLang);
            if (found is not null)
            {
                dictName = found.Code;
                ctx.Response.Cookies.Append(CookieName, found.Code, new CookieOptions
                {
                    MaxAge = TimeSpan.FromDays(365),
                    HttpOnly = false,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true
                });
            }
        }
        else if (ctx.Request.Cookies.TryGetValue(CookieName, out var cookieValue) &&
                 !string.IsNullOrEmpty(cookieValue))
        {
            dictName = cookieValue;
        }

        if (dictName is not null)
            ctx.Items[ItemsKey] = dictName;

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
