#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Translations;

public class TranslationsAccessor(LocalizerService localizerService, ILogger<TranslationsAccessor> logger)
{
    sealed class RequestLanguage
    {
        public LocalizerService.LoadedTranslations? Translations { get; set; }
    }

    readonly AsyncLocal<RequestLanguage?> _request = new();

    public LocalizerService.LoadedTranslations? RequestTranslations
    {
        get => _request.Value?.Translations;
        set
        {
            if (_request.Value is { } current)
                current.Translations = value;
            else
                _request.Value = new RequestLanguage { Translations = value };
        }
    }

    public IDisposable BeginRequest()
    {
        var previous = _request.Value;
        _request.Value = new RequestLanguage();
        return new Restore(() => _request.Value = previous);
    }

    sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    public LocalizerService.LoadedTranslations Current => RequestTranslations ?? localizerService.ServerTranslations;

    public async Task UseStoreLanguage(string? translationName)
    {
        if (string.IsNullOrEmpty(translationName))
        {
            RequestTranslations = new LocalizerService.LoadedTranslations(
                Translations.Default, Translations.Default, Translations.DefaultLanguage, false);
            return;
        }

        if (translationName == localizerService.ServerLanguage)
        {
            RequestTranslations = null;
            return;
        }
        try
        {
            if (await localizerService.GetUserTranslations(translationName) is { } translations)
                RequestTranslations = translations;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load the language {Language} of a store, the page keeps its current language", translationName);
        }
    }
}
