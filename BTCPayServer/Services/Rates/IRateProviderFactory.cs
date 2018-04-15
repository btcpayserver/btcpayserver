using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;

namespace BTCPayServer.Services.Rates
{
    public class RateRules : IEnumerable<RateRule>
    {
        private List<RateRule> rateRules;

        public RateRules()
        {
            rateRules = new List<RateRule>();
        }
        public RateRules(List<RateRule> rateRules)
        {
            this.rateRules = rateRules?.ToList() ?? new List<RateRule>();
        }
        public string PreferredExchange { get; set; }

        public IEnumerator<RateRule> GetEnumerator()
        {
            return rateRules.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
    public interface IRateProviderFactory
    {
        IRateProvider GetRateProvider(BTCPayNetwork network, RateRules rules);
        TimeSpan CacheSpan { get; set; }
        void InvalidateCache();
    }
}
