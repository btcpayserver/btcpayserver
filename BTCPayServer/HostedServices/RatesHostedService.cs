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

namespace BTCPayServer.HostedServices
{
    public class RatesHostedService : IHostedService
    {
        private SettingsRepository _SettingsRepository;
        private IRateProviderFactory _RateProviderFactory;
        private CoinAverageSettings _coinAverageSettings;
        public RatesHostedService(SettingsRepository repo,
                                  CoinAverageSettings coinAverageSettings,
                                  IRateProviderFactory rateProviderFactory)
        {
            this._SettingsRepository = repo;
            _RateProviderFactory = rateProviderFactory;
            _coinAverageSettings = coinAverageSettings;
        }


        CancellationTokenSource _Cts = new CancellationTokenSource();

        List<Task> _Tasks = new List<Task>();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Tasks.Add(RefreshCoinAverageSupportedExchanges(_Cts.Token));
            _Tasks.Add(RefreshCoinAverageSettings(_Cts.Token));
            return Task.CompletedTask;
        }


        async Task Timer(Func<Task> act, CancellationToken cancellation, [CallerMemberName]string caller = null)
        {
            await new SynchronizationContextRemover();
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await act();
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogWarning(ex, caller + " failed");
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellation);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                }
            }
        }
        Task RefreshCoinAverageSupportedExchanges(CancellationToken cancellation)
        {
            return Timer(async () =>
            {
                await new SynchronizationContextRemover();
                var tickers = await new CoinAverageRateProvider("BTC").GetExchangeTickersAsync();
                _coinAverageSettings.AvailableExchanges = tickers
                    .Exchanges
                    .Select(c => (c.DisplayName, c.Name))
                    .ToArray();

                await Task.Delay(TimeSpan.FromHours(5), cancellation);
            }, cancellation);
        }

        Task RefreshCoinAverageSettings(CancellationToken cancellation)
        {
            return Timer(async () =>
            {
                await new SynchronizationContextRemover();
                var rates = (await _SettingsRepository.GetSettingAsync<RatesSetting>()) ?? new RatesSetting();
                _RateProviderFactory.CacheSpan = TimeSpan.FromMinutes(rates.CacheInMinutes);
                if (!string.IsNullOrWhiteSpace(rates.PrivateKey) && !string.IsNullOrWhiteSpace(rates.PublicKey))
                {
                    _coinAverageSettings.KeyPair = (rates.PublicKey, rates.PrivateKey);
                }
                await _SettingsRepository.WaitSettingsChanged<RatesSetting>(cancellation);
            }, cancellation);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Cts.Cancel();
            return Task.WhenAll(_Tasks.ToArray());
        }
    }
}
