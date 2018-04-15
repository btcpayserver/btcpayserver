using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Rates
{
    public class MockRateProviderFactory : IRateProviderFactory
    {
        List<MockRateProvider> _Mocks = new List<MockRateProvider>();
        public MockRateProviderFactory()
        {

        }

        public TimeSpan CacheSpan { get; set; }

        public void AddMock(MockRateProvider mock)
        {
            _Mocks.Add(mock);
        }
        public IRateProvider GetRateProvider(BTCPayNetwork network, RateRules rules)
        {
            return _Mocks.FirstOrDefault(m => m.CryptoCode == network.CryptoCode);
        }

        public void InvalidateCache()
        {
            
        }
    }
    public class MockRateProvider : IRateProvider
    {
        List<Rate> _Rates;

        public string CryptoCode { get; }

        public MockRateProvider(string cryptoCode, params Rate[] rates)
        {
            _Rates = new List<Rate>(rates);
            CryptoCode = cryptoCode;
        }
        public MockRateProvider(string cryptoCode, List<Rate> rates)
        {
            _Rates = rates;
            CryptoCode = cryptoCode;
        }
        public Task<decimal> GetRateAsync(string currency)
        {
            var rate = _Rates.FirstOrDefault(r => r.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase));
            if (rate == null)
                throw new RateUnavailableException(currency);
            return Task.FromResult(rate.Value);
        }

        public Task<ICollection<Rate>> GetRatesAsync()
        {
            ICollection<Rate> rates = _Rates;
            return Task.FromResult(rates);
        }
    }
}
