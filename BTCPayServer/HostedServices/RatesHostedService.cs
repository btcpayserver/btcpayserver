using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.HostedServices
{
    public class RatesHostedService : BaseAsyncService
    {
        public class ExchangeRatesCache
        {
            [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
            public DateTimeOffset Created { get; set; }
            public List<BackgroundFetcherState> States { get; set; }
            public override string ToString()
            {
                return "";
            }
        }
        private readonly SettingsRepository _SettingsRepository;
        readonly RateProviderFactory _RateProviderFactory;

        public RatesHostedService(SettingsRepository repo,
                                  RateProviderFactory rateProviderFactory,
                                  Logs logs) : base(logs)
        {
            this._SettingsRepository = repo;
            _RateProviderFactory = rateProviderFactory;
        }

        internal override Task[] InitializeTasks()
        {
            return new Task[]
            {
                CreateLoopTask(RefreshRates)
            };
        }

        bool IsStillUsed(BackgroundFetcherRateProvider fetcher)
        {
            return fetcher.LastRequested is DateTimeOffset v &&
                   DateTimeOffset.UtcNow - v < TimeSpan.FromDays(1.0);
        }

        IEnumerable<(string ExchangeName, BackgroundFetcherRateProvider Fetcher)> GetStillUsedProviders()
        {
            foreach (var kv in _RateProviderFactory.Providers)
            {
                if (kv.Value is BackgroundFetcherRateProvider fetcher && IsStillUsed(fetcher))
                {
                    yield return (kv.Key, fetcher);
                }
            }
        }
        async Task RefreshRates()
        {
            var usedProviders = GetStillUsedProviders().ToArray();
            if (usedProviders.Length == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken);
                return;
            }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20.0));
                try
                {
                    await Task.WhenAll(usedProviders
                                    .Select(p => p.Fetcher.UpdateIfNecessary(timeout.Token).ContinueWith(t =>
                                    {
                                        if (t.Result.Exception != null && t.Result.Exception is not NotSupportedException)
                                        {
                                            Logs.PayServer.LogWarning($"Error while contacting exchange {p.ExchangeName}: {t.Result.Exception.Message}");
                                        }
                                    }, TaskScheduler.Default))
                                    .ToArray()).WithCancellation(timeout.Token);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                }
                if (_LastCacheDate is DateTimeOffset lastCache)
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
            await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await TryLoadRateCache();
            await base.StartAsync(cancellationToken);
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await SaveRateCache();
            await base.StopAsync(cancellationToken);
        }

        private async Task TryLoadRateCache()
        {
            try
            {
                var cache = await _SettingsRepository.GetSettingAsync<ExchangeRatesCache>();
                if (cache != null)
                {
                    _LastCacheDate = cache.Created;
                    var stateByExchange = cache.States.ToDictionary(o => o.ExchangeName);
                    foreach (var provider in _RateProviderFactory.Providers)
                    {
                        if (stateByExchange.TryGetValue(provider.Key, out var state) &&
                            provider.Value is BackgroundFetcherRateProvider fetcher)
                        {
                            fetcher.LoadState(state);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogWarning(ex, "Warning: Error while trying to load rates from cache");
            }
        }

        DateTimeOffset? _LastCacheDate;
        private async Task SaveRateCache()
        {
            var cache = new ExchangeRatesCache();
            cache.Created = DateTimeOffset.UtcNow;
            _LastCacheDate = cache.Created;

            var usedProviders = GetStillUsedProviders().ToArray();
            cache.States = new List<BackgroundFetcherState>(usedProviders.Length);
            foreach (var provider in usedProviders)
            {
                var state = provider.Fetcher.GetState();
                state.ExchangeName = provider.ExchangeName;
                cache.States.Add(state);
            }
            await _SettingsRepository.UpdateSetting(cache);
        }
    }
}
