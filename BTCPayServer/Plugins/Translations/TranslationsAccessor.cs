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

    public LocalizerService.LoadedTranslations? RequestTranslations => _request.Value?.Translations;

    public void UseStoreLanguage(LocalizerService.LoadedTranslations? translations)
    {
        if (_request.Value is { } current)
            current.Translations = translations;
        else
            _request.Value = new RequestLanguage { Translations = translations };
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
            return;

        if (translationName == localizerService.ServerLanguage)
        {
            UseStoreLanguage((LocalizerService.LoadedTranslations?)null);
            return;
        }
        try
        {
            if (await localizerService.GetUserTranslations(translationName) is { } translations)
                UseStoreLanguage(translations);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load the language {Language} of a store, the page keeps its current language", translationName);
        }
    }
}
