using System;
using NBitcoin;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Logging;
using BTCPayServer.Events;

namespace BTCPayServer.HostedServices
{
    public class RatesHostedService : BaseAsyncService
    {
        private SettingsRepository _SettingsRepository;
        private CoinAverageSettings _coinAverageSettings;
        private readonly EventAggregator _EventAggregator;
        RateProviderFactory _RateProviderFactory;
        public RatesHostedService(SettingsRepository repo,
                                  RateProviderFactory rateProviderFactory,
                                  CoinAverageSettings coinAverageSettings,
                                  EventAggregator eventAggregator)
        {
            this._SettingsRepository = repo;
            _coinAverageSettings = coinAverageSettings;
            _EventAggregator = eventAggregator;
            _RateProviderFactory = rateProviderFactory;
        }

        internal override Task[] InitializeTasks()
        {
            return new[]
            {
                CreateLoopTask(RefreshCoinAverageSupportedExchanges),
                ListenForRatesSettingChanges(),
                CreateLoopTask(RefreshRates)
            };
        }
        async Task RefreshRates()
        {

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20.0));
                try
                {
                    await Task.WhenAll(_RateProviderFactory.Providers
                                    .Select(p => (Fetcher: p.Value as BackgroundFetcherRateProvider, ExchangeName: p.Key)).Where(p => p.Fetcher != null)
                                    .Select(p => p.Fetcher.UpdateIfNecessary(timeout.Token).ContinueWith(t =>
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
            }
            await Task.Delay(TimeSpan.FromSeconds(30), Cancellation);
        }

        async Task RefreshCoinAverageSupportedExchanges()
        {
            var exchanges = new CoinAverageExchanges();
            foreach (var item in (await new CoinAverageRateProvider() { Authenticator = _coinAverageSettings }.GetExchangeTickersAsync())
                .Exchanges
                .Select(c => new CoinAverageExchange(c.Name, c.DisplayName, $"https://apiv2.bitcoinaverage.com/exchanges/{c.Name}")))
            {
                exchanges.Add(item);
            }
            _coinAverageSettings.AvailableExchanges = exchanges;
            await Task.Delay(TimeSpan.FromHours(5), Cancellation);
        }

        private IDisposable eventSubscription;

        async Task ListenForRatesSettingChanges()
        {
            var rates = (await _SettingsRepository.GetSettingAsync<RatesSetting>()) ?? new RatesSetting();
            OnNewRateSettingsData(rates);

            eventSubscription = _EventAggregator.Subscribe<SettingsChanged<RatesSetting>>(changed =>
            {
                OnNewRateSettingsData(changed.Settings);
            });
        }

        private void OnNewRateSettingsData(RatesSetting rates)
        {
            _RateProviderFactory.CacheSpan = TimeSpan.FromMinutes(rates.CacheInMinutes);
            if (!string.IsNullOrWhiteSpace(rates.PrivateKey) && !string.IsNullOrWhiteSpace(rates.PublicKey))
            {
                _coinAverageSettings.KeyPair = (rates.PublicKey, rates.PrivateKey);
            }
            else
            {
                _coinAverageSettings.KeyPair = null;
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            eventSubscription?.Dispose();
            return base.StopAsync(cancellationToken);
        }
    }
}
