using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
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

        public async Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            await new SynchronizationContextRemover();
            var rates = await _ExchangeAPI.GetTickersAsync();

                var exchangeRateTasks = rates
                    .Where(t => t.Value.Ask != 0m && t.Value.Bid != 0m)
                    .Select(t => CreateExchangeRate(t));

                var exchangeRates = await Task.WhenAll(exchangeRateTasks);
                
            return new ExchangeRates(exchangeRates
                .Where(t => t != null)
                .ToArray());
        }

        // ExchangeSymbolToGlobalSymbol throws exception which would kill perf
        ConcurrentDictionary<string, string> notFoundSymbols = new ConcurrentDictionary<string, string>();
        private async Task<ExchangeRate> CreateExchangeRate(KeyValuePair<string, ExchangeTicker> ticker)
        {
            if (notFoundSymbols.TryGetValue(ticker.Key, out _))
                return null;
            try
            {
                var tickerName = await _ExchangeAPI.ExchangeMarketSymbolToGlobalMarketSymbolAsync(ticker.Key);
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
