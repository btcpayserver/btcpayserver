#nullable enable
using System.Threading;

namespace BTCPayServer.Plugins.Translations;

public class TranslationsAccessor(LocalizerService localizerService)
{
    readonly AsyncLocal<LocalizerService.LoadedTranslations?> _requestTranslations = new();

    public LocalizerService.LoadedTranslations? RequestTranslations
    {
        get => _requestTranslations.Value;
        set => _requestTranslations.Value = value;
    }

    public LocalizerService.LoadedTranslations Current => RequestTranslations ?? localizerService.ServerTranslations;
}
