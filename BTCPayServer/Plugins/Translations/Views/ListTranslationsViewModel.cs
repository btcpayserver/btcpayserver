using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Translations.Views;

public class ListTranslationsViewModel
{
    public class TranslationViewModel
    {
        public bool Installed { get; set; }
        public string TranslationName { get; set; }
        public string NativeName { get; set; }
        public string MaintainerHandle { get; set; }
        public string MaintainerUrl { get; set; }
        public DateTimeOffset? LastUpdated { get; set; }
        public string Fallback { get; set; }
        public string Source { get; set; }
        public bool Editable { get; set; }
        public bool IsSelected { get; set; }
        public bool IsDownloadedLanguagePack { get; set; }
        public bool UpdateAvailable { get; set; }
    }

    public List<TranslationViewModel> InstalledLanguages { get; set; } = [];
    public List<TranslationViewModel> AvailableToInstall { get; set; } = [];
    public bool ManifestFetchFailed { get; set; }
}
