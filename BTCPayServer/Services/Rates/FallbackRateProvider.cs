using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;

namespace BTCPayServer.Services.Rates
{
    public class FallbackRateProvider : IRateProvider
    {
        IRateProvider[] _Providers;
        public FallbackRateProvider(IRateProvider[] providers)
        {
            if (providers == null)
                throw new ArgumentNullException(nameof(providers));
            _Providers = providers;
        }

        public async Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            foreach (var p in _Providers)
            {
                try
                {
                    return await p.GetRatesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch(Exception ex) { Exceptions.Add(ex); }
            }
            return new ExchangeRates();
        }

        public List<Exception> Exceptions { get; set; } = new List<Exception>();
    }
}
