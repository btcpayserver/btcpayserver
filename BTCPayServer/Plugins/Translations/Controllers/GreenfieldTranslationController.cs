#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Filters;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Translations.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
public class GreenfieldTranslationController(
    IEnumerable<IDefaultTranslationProvider> defaultTranslationProviders) : ControllerBase
{

    /// <summary>
    /// This route is used by the btcpayserver-translator project
    /// in order to extract all the strings that we need to translate.
    /// </summary>
    /// <returns></returns>
    [HttpGet("cheat/translations/default-en")]
    [AllowAnonymous]
    [CheatModeRoute]
    public async Task<IActionResult> GetDefaultEnglishTranslations()
    {
        var merged = new Dictionary<string, string>(Translations.Default.Records);
        foreach (var provider in defaultTranslationProviders)
        {
            foreach (var kv in await provider.GetDefaultTranslations())
                merged[kv.Key] = string.IsNullOrEmpty(kv.Value) ? kv.Key : kv.Value;
        }
        return new JsonResult(merged);
    }
}
