using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using ExchangeSharp;

namespace BTCPayServer.Services.Rates
{
    public class ExchangeSharpRateProvider<T> : IRateProvider where T : ExchangeAPI
    {
        readonly HttpClient _httpClient;
        public ExchangeSharpRateProvider(HttpClient httpClient)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            _httpClient = httpClient;
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            await new SynchronizationContextRemover();

            var exchangeAPI = (T)await ExchangeAPI.GetExchangeAPIAsync<T>();
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
        readonly ConcurrentDictionary<string, string> notFoundSymbols = new ConcurrentDictionary<string, string>();

        public RateSourceInfo RateSourceInfo { get; set; }

        private async Task<PairRate> CreateExchangeRate(T exchangeAPI, KeyValuePair<string, ExchangeTicker> ticker)
        {
            if (notFoundSymbols.TryGetValue(ticker.Key, out _))
                return null;
            try
            {
                CurrencyPair pair;
                if (ticker.Value.Volume.BaseCurrency is not null && ticker.Value.Volume.QuoteCurrency is not null)
                {
                    pair = new CurrencyPair(ticker.Value.Volume.BaseCurrency, ticker.Value.Volume.QuoteCurrency);
                }
                else
                {
                    var tickerName = await exchangeAPI.ExchangeMarketSymbolToGlobalMarketSymbolAsync(ticker.Key);
                    if (!CurrencyPair.TryParse(tickerName, out pair))
                    {
                        notFoundSymbols.TryAdd(ticker.Key, ticker.Key);
                        return null;
                    }
                }
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
