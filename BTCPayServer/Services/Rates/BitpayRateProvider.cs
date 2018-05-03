using NBitpayClient;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using BTCPayServer.Rating;

namespace BTCPayServer.Services.Rates
{
    public class BitpayRateProvider : IRateProvider
    {
        public const string BitpayName = "bitpay";
        Bitpay _Bitpay;
        public BitpayRateProvider(Bitpay bitpay)
        {
            if (bitpay == null)
                throw new ArgumentNullException(nameof(bitpay));
            _Bitpay = bitpay;
        }

        public async Task<ExchangeRates> GetRatesAsync()
        {
            return new ExchangeRates((await _Bitpay.GetRatesAsync().ConfigureAwait(false))
                .AllRates
                .Select(r => new ExchangeRate() { Exchange = BitpayName, CurrencyPair = new CurrencyPair("BTC", r.Code), Value = r.Value })
                .ToList());
        }
    }
}
