using BTCPayServer.Services;

namespace BTCPayServer.Models.ServerViewModels;

public class EditDictionaryViewModel
{
    public string Translations { get; set; }
    public int Lines { get; set; }
    public string Command { get; set; }

    internal EditDictionaryViewModel SetTranslations(Translations translations)
    {
        Translations = translations.ToTextFormat();
        Lines = translations.Records.Count;
        return this;
    }
}
