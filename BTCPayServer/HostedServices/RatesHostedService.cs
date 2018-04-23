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
            _coinAverageSettings.AvailableExchanges = new[] {
                (DisplayName: "BitBargain", Name: "bitbargain"),
                (DisplayName: "Tidex", Name: "tidex"),
                (DisplayName: "LocalBitcoins", Name: "localbitcoins"),
                (DisplayName: "EtherDelta", Name: "etherdelta"),
                (DisplayName: "Kraken", Name: "kraken"),
                (DisplayName: "BitBay", Name: "bitbay"),
                (DisplayName: "Independent Reserve", Name: "independentreserve"),
                (DisplayName: "Exmoney", Name: "exmoney"),
                (DisplayName: "Bitcoin.co.id", Name: "bitcoin_co_id"),
                (DisplayName: "Huobi", Name: "huobi"),
                (DisplayName: "GDAX", Name: "gdax"),
                (DisplayName: "Coincheck", Name: "coincheck"),
                (DisplayName: "Bittylicious", Name: "bittylicious"),
                (DisplayName: "Gemini", Name: "gemini"),
                (DisplayName: "QuadrigaCX", Name: "quadrigacx"),
                (DisplayName: "Bit2C", Name: "bit2c"),
                (DisplayName: "Luno", Name: "luno"),
                (DisplayName: "Negocie Coins", Name: "negociecoins"),
                (DisplayName: "FYB-SE", Name: "fybse"),
                (DisplayName: "Hitbtc", Name: "hitbtc"),
                (DisplayName: "Bitex.la", Name: "bitex"),
                (DisplayName: "Korbit", Name: "korbit"),
                (DisplayName: "itBit", Name: "itbit"),
                (DisplayName: "Okex", Name: "okex"),
                (DisplayName: "Bitsquare", Name: "bitsquare"),
                (DisplayName: "Bitfinex", Name: "bitfinex"),
                (DisplayName: "CoinMate", Name: "coinmate"),
                (DisplayName: "Bitstamp", Name: "bitstamp"),
                (DisplayName: "Cryptonit", Name: "cryptonit"),
                (DisplayName: "Foxbit", Name: "foxbit"),
                (DisplayName: "QuickBitcoin", Name: "quickbitcoin"),
                (DisplayName: "Poloniex", Name: "poloniex"),
                (DisplayName: "Bit-Z", Name: "bitz"),
                (DisplayName: "Liqui", Name: "liqui"),
                (DisplayName: "BitKonan", Name: "bitkonan"),
                (DisplayName: "Kucoin", Name: "kucoin"),
                (DisplayName: "Binance", Name: "binance"),
                (DisplayName: "Rock Trading", Name: "rocktrading"),
                (DisplayName: "Mercado Bitcoin", Name: "mercado"),
                (DisplayName: "Coinsecure", Name: "coinsecure"),
                (DisplayName: "Coinfloor", Name: "coinfloor"),
                (DisplayName: "bitFlyer", Name: "bitflyer"),
                (DisplayName: "BTCTurk", Name: "btcturk"),
                (DisplayName: "Bittrex", Name: "bittrex"),
                (DisplayName: "CampBX", Name: "campbx"),
                (DisplayName: "Zaif", Name: "zaif"),
                (DisplayName: "FYB-SG", Name: "fybsg"),
                (DisplayName: "Quoine", Name: "quoine"),
                (DisplayName: "BTC Markets", Name: "btcmarkets"),
                (DisplayName: "Bitso", Name: "bitso"),
                }.ToArray();
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
                var tickers = await new CoinAverageRateProvider("BTC") { Authenticator = _coinAverageSettings }.GetExchangeTickersAsync();
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
