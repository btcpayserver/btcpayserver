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

namespace BTCPayServer.HostedServices
{
    public class RatesHostedService : IHostedService
    {
        private SettingsRepository _SettingsRepository;
        private IRateProviderFactory _RateProviderFactory;
        public RatesHostedService(SettingsRepository repo, 
                                  IRateProviderFactory rateProviderFactory)
        {
            this._SettingsRepository = repo;
            _RateProviderFactory = rateProviderFactory;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Init();
            return Task.CompletedTask;
        }

        async void Init()
        {
            var rates = (await _SettingsRepository.GetSettingAsync<RatesSetting>()) ?? new RatesSetting();
            _RateProviderFactory.CacheSpan = TimeSpan.FromMinutes(rates.CacheInMinutes);

            //string[] availableExchanges = null;
            //// So we don't run this in testing
            //if(_RateProviderFactory is BTCPayRateProviderFactory)
            //{
            //    try
            //    {
            //        await new CoinAverageRateProvider("BTC").GetExchangeTickersAsync();
            //    }
            //    catch(Exception ex)
            //    {
            //        Logs.PayServer.LogWarning(ex, "Failed to get exchange tickers");
            //    }
            //}
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
