using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Rates
{
	public class MockRateProvider : IRateProvider
	{
		List<Rate> _Rates;

		public MockRateProvider(params Rate[] rates)
		{
			_Rates =  new List<Rate>(rates);
		}
		public MockRateProvider(List<Rate> rates)
		{
			_Rates = rates;
		}
		public Task<decimal> GetRateAsync(string currency)
		{
			var rate = _Rates.FirstOrDefault(r => r.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase));
			if(rate == null)
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
