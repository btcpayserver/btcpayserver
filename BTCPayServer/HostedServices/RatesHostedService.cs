using System;
using NBitcoin;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Hosting;
using BTCPayServer.Logging;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text;
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
        private SettingsRepository _SettingsRepository;
        RateProviderFactory _RateProviderFactory;
        public RatesHostedService(SettingsRepository repo,
                                  RateProviderFactory rateProviderFactory)
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
        async Task RefreshRates()
        {
            var usedProviders = _RateProviderFactory.Providers
                                    .Select(p => p.Value)
                                    .OfType<BackgroundFetcherRateProvider>()
                                    .Where(IsStillUsed)
                                    .ToArray();
            if (usedProviders.Length == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), Cancellation);
                return;
            }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20.0));
                try
                {
                    await Task.WhenAll(usedProviders
                                    .Select(p => p.UpdateIfNecessary(timeout.Token).ContinueWith(t =>
                                    {
                                        if (t.Result.Exception != null)
                                        {
                                            Logs.PayServer.LogWarning($"Error while contacting {p.ExchangeName}: {t.Result.Exception.Message}");
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
            await Task.Delay(TimeSpan.FromSeconds(30), Cancellation);
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
            var cache = await _SettingsRepository.GetSettingAsync<ExchangeRatesCache>();
            if (cache != null)
            {
                _LastCacheDate = cache.Created;
                var stateByExchange = cache.States.ToDictionary(o => o.ExchangeName);
                foreach (var obj in _RateProviderFactory.Providers
                                    .Select(p => p.Value)
                                    .OfType<BackgroundFetcherRateProvider>()
                                    .Select(v => (Fetcher: v, State: stateByExchange.TryGet(v.ExchangeName)))
                                    .Where(v => v.State != null))
                {
                    obj.Fetcher.LoadState(obj.State);
                }
            }
        }

        DateTimeOffset? _LastCacheDate;
        private async Task SaveRateCache()
        {
            var cache = new ExchangeRatesCache();
            cache.Created = DateTimeOffset.UtcNow;
            _LastCacheDate = cache.Created;
            cache.States = _RateProviderFactory.Providers
                                    .Select(p => p.Value)
                                    .OfType<BackgroundFetcherRateProvider>()
                                    .Where(IsStillUsed)
                                    .Select(p => p.GetState())
                                    .ToList();
            await _SettingsRepository.UpdateSetting(cache);
        }
    }
}
