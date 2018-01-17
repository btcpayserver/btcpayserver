using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;

namespace BTCPayServer.Services.Rates
{
    public class TweakRateProvider : IRateProvider
    {
        private BTCPayNetwork network;
        private IRateProvider rateProvider;
        private List<RateRule> rateRules;

        public TweakRateProvider(BTCPayNetwork network, IRateProvider rateProvider, List<RateRule> rateRules)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (rateProvider == null)
                throw new ArgumentNullException(nameof(rateProvider));
            if (rateRules == null)
                throw new ArgumentNullException(nameof(rateRules));
            this.network = network;
            this.rateProvider = rateProvider;
            this.rateRules = rateRules;
        }

        public async Task<decimal> GetRateAsync(string currency)
        {
            var rate = await rateProvider.GetRateAsync(currency);
            foreach(var rule in rateRules)
            {
                rate = rule.Apply(network, rate);
            }
            return rate;
        }

        public async Task<ICollection<Rate>> GetRatesAsync()
        {
            List<Rate> rates = new List<Rate>();
            foreach (var rate in await rateProvider.GetRatesAsync())
            {
                var localRate = rate.Value;
                foreach (var rule in rateRules)
                {
                    localRate = rule.Apply(network, localRate);
                }
                rates.Add(new Rate(rate.Currency, localRate));
            }
            return rates;
        }
    }
}
