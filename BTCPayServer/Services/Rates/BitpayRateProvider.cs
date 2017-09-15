using NBitpayClient;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Rates
{
	public class BitpayRateProvider : IRateProvider
	{
		Bitpay _Bitpay;
		public BitpayRateProvider(Bitpay bitpay)
		{
			if(bitpay == null)
				throw new ArgumentNullException(nameof(bitpay));
			_Bitpay = bitpay;
		}
		public async Task<decimal> GetRateAsync(string currency)
		{
			var rates = await _Bitpay.GetRatesAsync().ConfigureAwait(false);
			var rate = rates.GetRate(currency);
			if(rate == 0m)
				throw new RateUnavailableException(currency);
			return (decimal)rate;
		}

		public async Task<ICollection<Rate>> GetRatesAsync()
		{
			return (await _Bitpay.GetRatesAsync().ConfigureAwait(false))
				.AllRates
				.Select(r => new Rate() { Currency = r.Code, Value = r.Value })
				.ToList();
		}
	}
}
