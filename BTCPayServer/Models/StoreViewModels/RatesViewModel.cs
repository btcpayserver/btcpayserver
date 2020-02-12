using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class RatesViewModel
    {
        public class TestResultViewModel
        {
            public string CurrencyPair { get; set; }
            public string Rule { get; set; }
            public bool Error { get; set; }
        }
        public void SetExchangeRates(IEnumerable<AvailableRateProvider> supportedList, string preferredExchange)
        {
            var defaultStore = preferredExchange ?? CoinGeckoRateProvider.CoinGeckoName;
            supportedList = supportedList.Select(a => new AvailableRateProvider(a.Id, a.SourceId, GetName(a), a.Url, a.Source)).ToArray();
            var chosen = supportedList.FirstOrDefault(f => f.Id == defaultStore) ?? supportedList.FirstOrDefault();
            Exchanges = new SelectList(supportedList, nameof(chosen.Id), nameof(chosen.Name), chosen);
            PreferredExchange = chosen.Id;
            RateSource = chosen.Url;
        }

        private string GetName(AvailableRateProvider a)
        {
            switch (a.Source)
            {
                case Rating.RateSource.Direct:
                    return a.Name;
                case Rating.RateSource.Coingecko:
                    return $"{a.Name} (via CoinGecko)";
                default:
                    throw new NotSupportedException(a.Source.ToString());
            }
        }

        public List<TestResultViewModel> TestRateRules { get; set; }

        public SelectList Exchanges { get; set; }

        public bool ShowScripting { get; set; }

        [Display(Name = "Rate rules")]
        [MaxLength(2000)]
        public string Script { get; set; }
        public string DefaultScript { get; set; }
        public string ScriptTest { get; set; }
        public string DefaultCurrencyPairs { get; set; }
        public string StoreId { get; set; }
        public IEnumerable<AvailableRateProvider> AvailableExchanges { get; set; }

        [Display(Name = "Add a spread on exchange rate of ... %")]
        [Range(0.0, 100.0)]
        public double Spread
        {
            get;
            set;
        }

        [Display(Name = "Preferred price source (eg. bitfinex, bitstamp...)")]
        public string PreferredExchange { get; set; }

        public string RateSource
        {
            get;
            set;
        }
    }
}
