using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Translations.Views;

public class TranslationMetadataViewModel
{
    [Display(Name = "Language code")]
    public string Code { get; set; }

    [Display(Name = "Text direction")]
    public bool? Rtl { get; set; }

    public LanguageMetadata GetMetadata() => new(string.IsNullOrWhiteSpace(Code) ? null : Code.Trim(), Rtl);
}
