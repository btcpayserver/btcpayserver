using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Plugins.Translations.Views;
public class CreateTranslationViewModel
{
    [Required(AllowEmptyStrings = false)]
    public string Name { get; set; }
    public string Fallback { get; set; }
    public SelectListItem[] TranslationsListItems { get; set; }

    internal CreateTranslationViewModel SetTranslations(LocalizerService.Translation[] translations)
    {
        var items = translations.Select(d => new SelectListItem(d.TranslationName, d.TranslationName, d.TranslationName == Fallback)).ToArray();
        TranslationsListItems = items;
        return this;
    }
}
