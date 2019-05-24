﻿using NBitpayClient;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using System.Threading;

namespace BTCPayServer.Services.Rates
{
    public class BitpayRateProvider : IRateProvider, IHasExchangeName
    {
        public const string BitpayName = "bitpay";
        Bitpay _Bitpay;
        public BitpayRateProvider(Bitpay bitpay)
        {
            if (bitpay == null)
                throw new ArgumentNullException(nameof(bitpay));
            _Bitpay = bitpay;
        }

        public string ExchangeName => BitpayName;

        public async Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            return new ExchangeRates((await _Bitpay.GetRatesAsync().ConfigureAwait(false))
                .AllRates
                .Select(r => new ExchangeRate() { Exchange = BitpayName, CurrencyPair = new CurrencyPair("BTC", r.Code), BidAsk = new BidAsk(r.Value) })
                .ToList());
        }
    }
}
