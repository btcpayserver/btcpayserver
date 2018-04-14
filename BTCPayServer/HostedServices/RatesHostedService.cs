using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.HostedServices
{
    public class RatesHostedService : IHostedService
    {
        private SettingsRepository _SettingsRepository;
        private BTCPayRateProviderFactory _RateProviderFactory;
        public RatesHostedService(SettingsRepository repo, IRateProviderFactory rateProviderFactory)
        {
            this._SettingsRepository = repo;
            _RateProviderFactory = (BTCPayRateProviderFactory)rateProviderFactory;
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var rates = (await _SettingsRepository.GetSettingAsync<RatesSetting>()) ?? new RatesSetting();
            _RateProviderFactory.CacheSpan = TimeSpan.FromMinutes(rates.CacheInMinutes);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
