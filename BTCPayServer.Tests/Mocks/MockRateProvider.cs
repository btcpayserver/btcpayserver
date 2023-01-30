using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Tests.Mocks
{
    public class MockRateProvider : IRateProvider
    {
        public List<PairRate> ExchangeRates { get; set; } = new List<PairRate>();

        public RateSourceInfo RateSourceInfo => new RateSourceInfo("mock", "Mock", "https://mock.rf");

        public MockRateProvider()
        {

        }
        public Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ExchangeRates.ToArray());
        }
    }
}
