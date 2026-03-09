using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Translations.Views;

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
