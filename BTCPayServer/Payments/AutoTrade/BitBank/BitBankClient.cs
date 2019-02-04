using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using BitBankApi;
using NBitcoin;

namespace BTCPayServer.Payments.AutoTrade.BitBank
{
    public class BitBankClient : IAutoTradeExchangeClient
    {
        private readonly string _apisecret;
        private readonly bool _showFiat;
        private readonly PrivateApi _client;
        private readonly string _currencyTypeToBuy;
        private static List<string> SupportedCurrencies = new List<string> { "btc", "ltc" };

        public BitBankClient(string apiKey, string apiSecret, string apiUrl,string currencyTypeToBuy = "jpy", bool showFiat = true)
        {
            if (currencyTypeToBuy != "jpy")
            {
                throw new ArgumentException($"{currencyTypeToBuy} is not supported currency type in BitBank!");
            }
            _apisecret = apiSecret;
            _currencyTypeToBuy = currencyTypeToBuy;
            _showFiat = showFiat;
            _client = new PrivateApi(apiKey, apiSecret);
        }


        public async Task<bool> Sell(string cryptoCode, Money amount, decimal? expectedPrice = null)
        {
            var lowerCC = cryptoCode.ToLower();
            if (!SupportedCurrencies.Contains(lowerCC))
            {
                throw new ArgumentException($"{cryptoCode} is not supported in BitBank");
            }
            var currencyPair = _currencyTypeToBuy + lowerCC;
            var amountInBTCString = amount.ToUnit(MoneyUnit.BTC).ToString();
            await _client.PostOrderAsync(currencyPair, amountInBTCString, "sell", "market");
            return true;
        }
    }
}
