using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Translations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Hosting;

public class UserLanguageMiddleware(
    RequestDelegate next,
    LocalizerService localizer,
    TranslationsAccessor translationsAccessor,
    ILogger<UserLanguageMiddleware> logger)
{
    public async Task Invoke(HttpContext httpContext, UserManager<ApplicationUser> userManager)
    {
        using (translationsAccessor.BeginRequest())
        {
            if (httpContext.User.Identity is { IsAuthenticated: true } identity &&
                identity.AuthenticationType == IdentityConstants.ApplicationScheme)
            {
                try
                {
                    translationsAccessor.RequestTranslations = await localizer.GetTranslationsForUser(httpContext.User, userManager);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to resolve the user's language, using the server language");
                }
            }

            await next(httpContext);
        }
    }
}
