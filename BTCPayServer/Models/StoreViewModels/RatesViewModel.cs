using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
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
        class Format
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
        public void SetExchangeRates(CoinAverageExchange[] supportedList, string preferredExchange)
        {
            var defaultStore = preferredExchange ?? CoinAverageRateProvider.CoinAverageName;
            var choices = supportedList.Select(o => new Format() { Name = o.Display, Value = o.Name }).ToArray();
            var chosen = choices.FirstOrDefault(f => f.Value == defaultStore) ?? choices.FirstOrDefault();
            Exchanges = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
            PreferredExchange = chosen.Value;
        }

        public List<TestResultViewModel> TestRateRules { get; set; }

        public SelectList Exchanges { get; set; }

        public bool ShowScripting { get; set; }

        [Display(Name = "Rate rules")]
        [MaxLength(2000)]
        public string Script { get; set; }
        public string DefaultScript { get; set; }
        public string ScriptTest { get; set; }
        public CoinAverageExchange[] AvailableExchanges { get; set; }

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
            get
            {
                return PreferredExchange == CoinAverageRateProvider.CoinAverageName ? "https://apiv2.bitcoinaverage.com/indices/global/ticker/short" : $"https://apiv2.bitcoinaverage.com/exchanges/{PreferredExchange}";
            }
        }
    }
}
