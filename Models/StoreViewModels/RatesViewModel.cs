using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Rating;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class RatesViewModel
    {
        public class Source
        {
            public bool ShowScripting { get; set; }

            [Display(Name = "Rate Rules")]
            [MaxLength(2000)]
            public string Script { get; set; }

            public string DefaultScript { get; set; }

            [Display(Name = "Preferred Price Source")]
            public string PreferredExchange { get; set; }

            public SelectList Exchanges { get; set; }
            public string RateSource { get; set; }
            public string PreferredResolvedExchange { get; set; }
            public bool IsFallback { get; set; }
            public ConfirmModel ScriptingConfirm { get; set; }
        }

        public class TestResultViewModel
        {
            public string CurrencyPair { get; set; }
            public string Rule { get; set; }
            public bool Error { get; set; }
        }

        public List<TestResultViewModel> TestRateRules { get; set; }
        public string Hash { get; set; }

        public Source PrimarySource { get; set; }
        public Source FallbackSource { get; set; }

        [Display(Name = "Enable fallback rates")]
        public bool HasFallback { get; set; }

        public string ScriptTest { get; set; }
        public string DefaultCurrencyPairs { get; set; }
        public string StoreId { get; set; }

        [Display(Name = "Add Exchange Rate Spread")]
        [Range(0.0, 100.0)]
        public double Spread { get; set; }

        public IEnumerable<RateSourceInfo> AvailableExchanges { get; set; }
    }
}
