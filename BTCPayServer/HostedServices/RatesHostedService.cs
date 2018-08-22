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
                CreateLoopTask(RefreshCoinAverageSettings)
            };
        }

        async Task RefreshCoinAverageSupportedExchanges()
        {
            await new SynchronizationContextRemover();
            var tickers = await new CoinAverageRateProvider() { Authenticator = _coinAverageSettings }.GetExchangeTickersAsync();
            var exchanges = new CoinAverageExchanges();
            foreach(var item in tickers
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
            await new SynchronizationContextRemover();
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
