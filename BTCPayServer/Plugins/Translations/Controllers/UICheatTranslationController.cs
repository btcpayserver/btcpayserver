#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Filters;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Translations.Controllers;

[AllowAnonymous]
[Area(TranslationsPlugin.Area)]
public class UICheatTranslationController(
    IEnumerable<IDefaultTranslationProvider> defaultTranslationProviders) : Controller
{
    [HttpGet("cheat/translations/default-en")]
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
