#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.HostedServices
{
    public class RatesHostedService(
        IOptions<DataDirectories> dataDirectories,
        RateProviderFactory rateProviderFactory) : IHostedService, IPeriodicTask
    {
        public class ExchangeRatesCache
        {
            [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
            public DateTimeOffset Created { get; set; }
            public List<BackgroundFetcherState>? States { get; set; }
            public override string ToString()
            {
                return "";
            }
        }

        bool IsStillUsed(BackgroundFetcherRateProvider fetcher)
        {
            return fetcher.LastRequested is DateTimeOffset v &&
                   DateTimeOffset.UtcNow - v < TimeSpan.FromDays(1.0);
        }

        IEnumerable<(string ExchangeName, BackgroundFetcherRateProvider Fetcher)> GetStillUsedProviders()
        {
            foreach (var kv in rateProviderFactory.Providers)
            {
                if (kv.Value is BackgroundFetcherRateProvider fetcher && IsStillUsed(fetcher))
                {
                    yield return (kv.Key, fetcher);
                }
            }
        }
        public async Task Do(CancellationToken cancellationToken)
        {
            var usedProviders = GetStillUsedProviders().ToArray();
            if (usedProviders.Length == 0)
                return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20.0));
            try
            {
                await Task.WhenAll(usedProviders
                    .Select(p => p.Fetcher.UpdateIfNecessary(timeout.Token))
                    .ToArray()).WithCancellation(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
            }
            if (_lastCacheDate is DateTimeOffset lastCache)
            {
                if (DateTimeOffset.UtcNow - lastCache > TimeSpan.FromMinutes(8.0))
                {
                    await SaveRateCache();
                }
            }
            else
            {
                await SaveRateCache();
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await TryLoadRateCache();
        }
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await SaveRateCache();
        }

        private async Task TryLoadRateCache()
        {
            ExchangeRatesCache? cache = null;
            try
            {
                cache = JsonConvert.DeserializeObject<ExchangeRatesCache>(await File.ReadAllTextAsync(GetRatesCacheFilePath(), new UTF8Encoding(false)));
            }
            catch
            {
            }
            if (cache is { States: not null })
            {
                _lastCacheDate = cache.Created;
                var stateByExchange = cache.States.ToDictionary(o => o.ExchangeName);
                foreach (var kv in stateByExchange)
                {
                    if (rateProviderFactory.Providers.TryGetValue(kv.Key, out var prov) &&
                        prov is BackgroundFetcherRateProvider fetcher)
                    {
                        fetcher.LoadState(kv.Value);
                    }
                }
            }
        }

        DateTimeOffset? _lastCacheDate;
        private async Task SaveRateCache()
        {
            var cache = new ExchangeRatesCache();
            cache.Created = DateTimeOffset.UtcNow;
            _lastCacheDate = cache.Created;

            var usedProviders = GetStillUsedProviders().ToArray();
            cache.States = new List<BackgroundFetcherState>(usedProviders.Length);
            foreach (var provider in usedProviders)
            {
                var state = provider.Fetcher.GetState();
                state.ExchangeName = provider.ExchangeName;
                cache.States.Add(state);
            }

            await File.WriteAllTextAsync(GetRatesCacheFilePath(), JsonConvert.SerializeObject(cache), new UTF8Encoding(false));
        }

        private string GetRatesCacheFilePath() => Path.Combine(dataDirectories.Value.DataDir, "rates-cache.json");
    }
}
