using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Tests.Mocks
{
    public class MockRateProvider : CoinGeckoRateProvider
    {
        public ExchangeRates ExchangeRates { get; set; } = new ExchangeRates();
        public List<AvailableRateProvider> AvailableRateProviders { get; set; } = new List<AvailableRateProvider>();

        public MockRateProvider():base(null)
        {
            
        }
        public override Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ExchangeRates);
        }

        public override Task<IEnumerable<AvailableRateProvider>> GetAvailableExchanges(bool reload = false)
        {
            return Task.FromResult((IEnumerable<AvailableRateProvider>)AvailableRateProviders);
        }
    }
}
