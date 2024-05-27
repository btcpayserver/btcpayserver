#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using BTCPayApp.CommonServer.Models;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.App.API;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldBearer)]
[Route("btcpayapp")]
public partial class AppApiController(
    StoreRepository storeRepository,
    EventAggregator eventAggregator,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    ISettingsRepository settingsRepository,
    UriResolver uriResolver,
    IOptionsMonitor<BearerTokenOptions> bearerTokenOptions)
    : Controller
{
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
