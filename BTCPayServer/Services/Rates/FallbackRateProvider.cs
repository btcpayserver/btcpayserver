using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Rating;

namespace BTCPayServer.Services.Rates
{
    public class FallbackRateProvider : IRateProvider
    {
        IRateProvider[] _Providers;
        public bool Used { get; set; }
        public FallbackRateProvider(IRateProvider[] providers)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            _Providers = providers;
        }

        public async Task<ExchangeRates> GetRatesAsync()
        {
            Used = true;
            foreach (var p in _Providers)
            {
                try
                {
                    return await p.GetRatesAsync().ConfigureAwait(false);
                }
                catch(Exception ex) { Exceptions.Add(ex); }
            }
            return new ExchangeRates();
        }

        public List<Exception> Exceptions { get; set; } = new List<Exception>();
    }
}
