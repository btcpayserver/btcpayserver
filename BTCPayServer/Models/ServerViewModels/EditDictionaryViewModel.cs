using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services;

namespace BTCPayServer.Models.ServerViewModels;

public class EditDictionaryViewModel
{
    [Display(Name = "Translations")]
    public string Translations { get; set; }
    public int Lines { get; set; }
    public string Command { get; set; }

    internal EditDictionaryViewModel SetTranslations(Translations translations)
    {
        Translations = translations.ToJsonFormat();
        Lines = translations.Records.Count;
        return this;
    }
}
