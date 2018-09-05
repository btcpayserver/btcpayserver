using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using ExchangeSharp;

namespace BTCPayServer.Services.Rates
{
    public class ExchangeSharpRateProvider : IRateProvider, IHasExchangeName
    {
        readonly ExchangeAPI _ExchangeAPI;
        readonly string _ExchangeName;
        public ExchangeSharpRateProvider(string exchangeName, ExchangeAPI exchangeAPI, bool reverseCurrencyPair = false)
        {
            if (exchangeAPI == null)
                throw new ArgumentNullException(nameof(exchangeAPI));
            exchangeAPI.RequestTimeout = TimeSpan.FromSeconds(5.0);
            _ExchangeAPI = exchangeAPI;
            _ExchangeName = exchangeName;
            ReverseCurrencyPair = reverseCurrencyPair;
        }

        public bool ReverseCurrencyPair
        {
            get; set;
        }

        public string ExchangeName => _ExchangeName;

        public async Task<ExchangeRates> GetRatesAsync()
        {
            await new SynchronizationContextRemover();
            var rates = await _ExchangeAPI.GetTickersAsync();
            lock (notFoundSymbols)
            {
                var exchangeRates =
                    rates.Select(t => CreateExchangeRate(t))
                    .Where(t => t != null)
                    .ToArray();
                return new ExchangeRates(exchangeRates);
            }
        }

        // ExchangeSymbolToGlobalSymbol throws exception which would kill perf
        ConcurrentDictionary<string, string> notFoundSymbols = new ConcurrentDictionary<string, string>();
        private ExchangeRate CreateExchangeRate(KeyValuePair<string, ExchangeTicker> ticker)
        {
            if (notFoundSymbols.ContainsKey(ticker.Key))
                return null;
            try
            {
                var tickerName = _ExchangeAPI.ExchangeSymbolToGlobalSymbol(ticker.Key);
                if (!CurrencyPair.TryParse(tickerName, out var pair))
                {
                    notFoundSymbols.TryAdd(ticker.Key, ticker.Key);
                    return null;
                }
                if(ReverseCurrencyPair)
                    pair = new CurrencyPair(pair.Right, pair.Left);
                var rate = new ExchangeRate();
                rate.CurrencyPair = pair;
                rate.Exchange = _ExchangeName;
                rate.BidAsk = new BidAsk(ticker.Value.Bid, ticker.Value.Ask);
                return rate;
            }
            catch (ArgumentException)
            {
                notFoundSymbols.TryAdd(ticker.Key, ticker.Key);
                return null;
            }
        }
    }
}
