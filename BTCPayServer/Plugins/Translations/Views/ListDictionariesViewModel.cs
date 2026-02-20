using System.Collections.Generic;

namespace BTCPayServer.Plugins.Translations.Views;

public class ListDictionariesViewModel
{
    public class DictionaryViewModel
    {
        public string DictionaryName { get; set; }
        public string Fallback { get; set; }
        public string Source { get; set; }
        public bool Editable { get; set; }
        public bool IsSelected { get; set; }
        public bool IsDownloadedLanguagePack { get; set; }
        public bool UpdateAvailable { get; set; }
    }

    public List<DictionaryViewModel> Dictionaries = [];
}
