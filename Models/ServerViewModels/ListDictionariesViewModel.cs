using System.Collections.Generic;

namespace BTCPayServer.Models.ServerViewModels;

public class ListDictionariesViewModel
{
    public class DictionaryViewModel
    {
        public string DictionaryName { get; set; }
        public string Fallback { get; set; }
        public string Source { get; set; }
        public bool Editable { get; set; }
        public bool IsSelected { get; set; }
    }

    public List<DictionaryViewModel> Dictionaries = [];
}
