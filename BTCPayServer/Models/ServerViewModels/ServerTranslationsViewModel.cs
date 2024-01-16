using System;
using BTCPayServer.Services;

namespace BTCPayServer.Models.ServerViewModels
{
    public class ServerTranslationsViewModel
    {
        public string Translations { get; set; }
        public int Lines { get; set; }

        internal ServerTranslationsViewModel SetTranslations(Translations translations)
        {
            Translations = translations.ToTextFormat();
            Lines = translations.Records.Count;
            return this;
        }
    }
}
