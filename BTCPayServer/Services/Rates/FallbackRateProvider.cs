using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Rates
{
    public class FallbackRateProviderDescription : RateProviderDescription
    {
        public FallbackRateProviderDescription(RateProviderDescription[] rateProviders)
        {
            RateProviders = rateProviders;
        }

        public RateProviderDescription[] RateProviders { get; set; }

        public IRateProvider CreateRateProvider(IServiceProvider serviceProvider)
        {
            return new FallbackRateProvider(RateProviders.Select(r => r.CreateRateProvider(serviceProvider)).ToArray());
        }
    }

    public class FallbackRateProvider : IRateProvider
    {

        IRateProvider[] _Providers;
        public FallbackRateProvider(IRateProvider[] providers)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            _Providers = providers;
        }
        public async Task<decimal> GetRateAsync(string currency)
        {
            foreach(var p in _Providers)
            {
                try
                {
                    return await p.GetRateAsync(currency).ConfigureAwait(false);
                }
                catch { }
            }
            throw new RateUnavailableException(currency);
        }

        public async Task<ICollection<Rate>> GetRatesAsync()
        {
            foreach (var p in _Providers)
            {
                try
                {
                    return await p.GetRatesAsync().ConfigureAwait(false);
                }
                catch { }
            }
            throw new RateUnavailableException("ALL");
        }
    }
}
