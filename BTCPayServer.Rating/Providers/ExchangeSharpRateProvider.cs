using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using ExchangeSharp;

namespace BTCPayServer.Services.Rates
{
    public class ExchangeSharpRateProvider<T> : IRateProvider where T : ExchangeAPI, new()
    {
        HttpClient _httpClient;
        public ExchangeSharpRateProvider(HttpClient httpClient, bool reverseCurrencyPair = false)
        {
            if (httpClient == null)
                throw new ArgumentNullException(nameof(httpClient));
            ReverseCurrencyPair = reverseCurrencyPair;
            _httpClient = httpClient;
        }

        public bool ReverseCurrencyPair
        {
            get; set;
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            await new SynchronizationContextRemover();

            var exchangeAPI = new T();
            exchangeAPI.RequestMaker = new HttpClientRequestMaker(exchangeAPI, _httpClient, cancellationToken);
            var rates = await exchangeAPI.GetTickersAsync();

            var exchangeRateTasks = rates
                .Where(t => t.Value.Ask != 0m && t.Value.Bid != 0m)
                .Select(t => CreateExchangeRate(exchangeAPI, t));

            var exchangeRates = await Task.WhenAll(exchangeRateTasks);

            return exchangeRates
                .Where(t => t != null)
                .ToArray();
        }

        // ExchangeSymbolToGlobalSymbol throws exception which would kill perf
        ConcurrentDictionary<string, string> notFoundSymbols = new ConcurrentDictionary<string, string>();
        private async Task<PairRate> CreateExchangeRate(T exchangeAPI, KeyValuePair<string, ExchangeTicker> ticker)
        {
            if (notFoundSymbols.TryGetValue(ticker.Key, out _))
                return null;
            try
            {
                var tickerName = await exchangeAPI.ExchangeMarketSymbolToGlobalMarketSymbolAsync(ticker.Key);
                if (!CurrencyPair.TryParse(tickerName, out var pair))
                {
                    notFoundSymbols.TryAdd(ticker.Key, ticker.Key);
                    return null;
                }
                if (ReverseCurrencyPair)
                    pair = new CurrencyPair(pair.Right, pair.Left);
                return new PairRate(pair, new BidAsk(ticker.Value.Bid, ticker.Value.Ask));
            }
            catch (ArgumentException)
            {
                notFoundSymbols.TryAdd(ticker.Key, ticker.Key);
                return null;
            }
        }
    }
}
