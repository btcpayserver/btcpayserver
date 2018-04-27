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

        internal override Task[] initializeTasks()
        {
            return new[]
            {
                createLoopTask(RefreshCoinAverageSupportedExchanges),
                createLoopTask(RefreshCoinAverageSettings)
            };
        }

        async Task RefreshCoinAverageSupportedExchanges()
        {
            await new SynchronizationContextRemover();
            var tickers = await new CoinAverageRateProvider("BTC") { Authenticator = _coinAverageSettings }.GetExchangeTickersAsync();
            _coinAverageSettings.AvailableExchanges = tickers
                .Exchanges
                .Select(c => (c.DisplayName, c.Name))
                .ToArray();
            await Task.Delay(TimeSpan.FromHours(5), _SyncToken);
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
            await _SettingsRepository.WaitSettingsChanged<RatesSetting>(_SyncToken);
        }
    }
}
