#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.App.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Fido2;
using BTCPayServer.Logging;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.App.API;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
[Route("btcpayapp")]
public partial class AppApiController(
    IHttpContextAccessor httpContextAccessor,
    GreenfieldUsersController greenfieldUsersController,
    StoreRepository storeRepository,
    AppService appService,
    EventAggregator eventAggregator,
    CallbackGenerator callbackGenerator,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    APIKeyRepository apiKeyRepository,
    SettingsRepository settingsRepository,
    UriResolver uriResolver,
    DefaultRulesCollection defaultRules,
    RateFetcher rateFactory,
    UserLoginCodeService userLoginCodeService,
    Logs logs)
    : Controller
{
    private readonly ILogger _logger = logs.PayServer;
    
    [AllowAnonymous]
    [HttpGet("instance")]
    public async Task<Results<Ok<AppInstanceInfo>, NotFound>> Instance()
    {
        var serverSettings = await settingsRepository.GetSettingAsync<ServerSettings>() ?? new ServerSettings();
        var policiesSettings = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
        var themeSettings = await settingsRepository.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();

        return TypedResults.Ok(new AppInstanceInfo
        {
            BaseUrl = Request.GetAbsoluteRoot(),
            ServerName = serverSettings.ServerName,
            ContactUrl = serverSettings.ContactUrl,
            RegistrationEnabled = policiesSettings.EnableRegistration,
            CustomThemeExtension = themeSettings.CustomTheme ? themeSettings.CustomThemeExtension.ToString() : null,
            CustomThemeCssUrl = themeSettings.CustomTheme && !string.IsNullOrEmpty(themeSettings.CustomThemeCssUrl?.ToString())
                ? await uriResolver.Resolve(Request.GetAbsoluteRootUri(), themeSettings.CustomThemeCssUrl)
                : null,
            LogoUrl = !string.IsNullOrEmpty(themeSettings.LogoUrl?.ToString())
                ? await uriResolver.Resolve(Request.GetAbsoluteRootUri(), themeSettings.LogoUrl)
                : null
        });
    }
}
