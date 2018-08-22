using System;
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

namespace BTCPayServer.HostedServices
{
    public class RatesHostedService : BaseAsyncService
    {
        private SettingsRepository _SettingsRepository;
        private CoinAverageSettings _coinAverageSettings;
        RateProviderFactory _RateProviderFactory;
        public RatesHostedService(SettingsRepository repo,
                                  RateProviderFactory rateProviderFactory,
                                  CoinAverageSettings coinAverageSettings)
        {
            this._SettingsRepository = repo;
            _coinAverageSettings = coinAverageSettings;
            _RateProviderFactory = rateProviderFactory;
        }

        internal override Task[] InitializeTasks()
        {
            return new[]
            {
                CreateLoopTask(RefreshCoinAverageSupportedExchanges),
                CreateLoopTask(RefreshCoinAverageSettings),
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
                                    .Select(p => p.Fetcher.UpdateIfNecessary().ContinueWith(t =>
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
            var tickers = await new CoinAverageRateProvider() { Authenticator = _coinAverageSettings }.GetExchangeTickersAsync();
            var exchanges = new CoinAverageExchanges();
            foreach (var item in tickers
                .Exchanges
                .Select(c => new CoinAverageExchange(c.Name, c.DisplayName)))
            {
                exchanges.Add(item);
            }
            _coinAverageSettings.AvailableExchanges = exchanges;
            await Task.Delay(TimeSpan.FromHours(5), Cancellation);
        }

        async Task RefreshCoinAverageSettings()
        {
            var rates = (await _SettingsRepository.GetSettingAsync<RatesSetting>()) ?? new RatesSetting();
            _RateProviderFactory.CacheSpan = TimeSpan.FromMinutes(rates.CacheInMinutes);
            if (!string.IsNullOrWhiteSpace(rates.PrivateKey) && !string.IsNullOrWhiteSpace(rates.PublicKey))
            {
                _coinAverageSettings.KeyPair = (rates.PublicKey, rates.PrivateKey);
            }
            else
            {
                _coinAverageSettings.KeyPair = null;
            }
            await _SettingsRepository.WaitSettingsChanged<RatesSetting>(Cancellation);
        }
    }
}
