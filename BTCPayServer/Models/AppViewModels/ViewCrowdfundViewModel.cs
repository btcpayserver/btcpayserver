using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Models.AppViewModels
{
    public class ViewCrowdfundViewModel
    {
        public string HubPath { get; set; }
        public string StoreId { get; set; }
        public string AppId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string MainImageUrl { get; set; }
        public string EmbeddedCSS { get; set; }
        public string CustomCSSLink { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public string TargetCurrency { get; set; }
        public decimal? TargetAmount { get; set; }
        public bool EnforceTargetAmount { get; set; }

        public CrowdfundInfo Info { get; set; }
        public string Tagline { get; set; }
        public ViewPointOfSaleViewModel.Item[] Perks { get; set; }
        public bool DisqusEnabled { get; set; }
        public bool SoundsEnabled { get; set; }
        public string DisqusShortname { get; set; }
        public bool AnimationsEnabled { get; set; }
        public string[] AnimationColors { get; set; }
        public string[] Sounds { get; set; }
        public int ResetEveryAmount { get; set; }
        public bool NeverReset { get; set; }

        public Dictionary<string, int> PerkCount { get; set; }

        public CurrencyData CurrencyData { get; set; }

        public class CrowdfundInfo
        {
            public int TotalContributors { get; set; }
            public decimal CurrentPendingAmount { get; set; }
            public decimal CurrentAmount { get; set; }
            public decimal? ProgressPercentage { get; set; }
            public decimal? PendingProgressPercentage { get; set; }
            public DateTime LastUpdated { get; set; }
            public Dictionary<string, decimal> PaymentStats { get; set; }
            public Dictionary<string, decimal> PendingPaymentStats { get; set; }
            public DateTime? LastResetDate { get; set; }
            public DateTime? NextResetDate { get; set; }
        }
        public class Contribution
        {
            public PaymentMethodId PaymentMethodId { get; set; }
            public decimal Value { get; set; }
            public decimal CurrencyValue { get; set; }
            public string Currency { get; set; }
        }
        public class Contributions
        {
            public Dictionary<string, Dictionary<PaymentMethodId, Contribution>> Collection { get; }

            
            public Contributions(Dictionary<string, Dictionary<PaymentMethodId, Contribution>> collection)
            {
                Collection = collection;
            }

            public async Task<decimal> GetTotalCurrency(string targetCurrency, RateRules rateRules, RateFetcher rateFetcher)
            {
                var currencyPairs = Collection.Keys.Select(s => new CurrencyPair(targetCurrency, s)).ToHashSet();
                var rates = rateFetcher.FetchRates(currencyPairs, rateRules, CancellationToken.None);
                await Task.WhenAll(rates.Values);
                return Collection.Sum(pair =>
                {
                    var rate = rates[new CurrencyPair(targetCurrency, pair.Key)].Result;
                    if (rate.BidAsk is null)
                    {
                        return 0;
                    }
                    return pair.Value.Sum(valuePair => valuePair.Value.CurrencyValue) / rate.BidAsk.Bid;
                });
            }

            public Dictionary<PaymentMethodId, Contribution> GetTotalPaymentMethodContributions()
            {
                return Collection.Select(pair => pair.Value).SelectMany(dictionary => dictionary.Select(pair => pair)).GroupBy(pair => pair.Key)
                    .ToDictionary(pairs => 
                        pairs.Key, 
                        pairs => 
                            new Contribution()
                            {
                                Value = pairs.Sum(pair => pair.Value.Value),
                                PaymentMethodId = pairs.Key
                            });
            }
            
        }

        public bool Started => !StartDate.HasValue || DateTime.UtcNow > StartDate;

        public bool Ended => EndDate.HasValue && DateTime.UtcNow > EndDate;
        public bool DisplayPerksRanking { get; set; }
        public bool Enabled { get; set; }
        public string ResetEvery { get; set; }
        public Dictionary<string, CurrencyData> CurrencyDataPayments { get; set; }
    }

    public class ContributeToCrowdfund
    {
        public ViewCrowdfundViewModel ViewCrowdfundViewModel { get; set; }
        [Required] public decimal Amount { get; set; }
        public string Email { get; set; }
        public string ChoiceKey { get; set; }
        public bool RedirectToCheckout { get; set; }
        public string RedirectUrl { get; set; }
    }
}
