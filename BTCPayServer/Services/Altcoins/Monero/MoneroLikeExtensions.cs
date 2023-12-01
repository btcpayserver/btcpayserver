#if ALTCOINS
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Configuration;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Services.Altcoins.Monero.Configuration;
using BTCPayServer.Services.Altcoins.Monero.Payments;
using BTCPayServer.Services.Altcoins.Monero.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Services.Altcoins.Monero
{
    public static class MoneroLikeExtensions
    {
        public static IServiceCollection AddMoneroLike(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton(provider =>
                provider.ConfigureMoneroLikeConfiguration());
            serviceCollection.AddHttpClient("XMRclient")
                .ConfigurePrimaryHttpMessageHandler(provider =>
                {
                    var configuration = provider.GetRequiredService<MoneroLikeConfiguration>();
                    if(!configuration.MoneroLikeConfigurationItems.TryGetValue("XMR", out var xmrConfig) || xmrConfig.Username is null || xmrConfig.Password is null){
                        return new HttpClientHandler();
                    }
                    return new HttpClientHandler
                    {
                        Credentials = new NetworkCredential(xmrConfig.Username, xmrConfig.Password),
                        PreAuthenticate = true
                    };
                });
            serviceCollection.AddSingleton<MoneroRPCProvider>();
            serviceCollection.AddHostedService<MoneroLikeSummaryUpdaterHostedService>();
            serviceCollection.AddHostedService<MoneroListener>();
            serviceCollection.AddSingleton<MoneroLikePaymentMethodHandler>();
            serviceCollection.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<MoneroLikePaymentMethodHandler>());
            serviceCollection.AddSingleton<IUIExtension>(new UIExtension("Monero/StoreNavMoneroExtension",  "store-nav"));
            serviceCollection.AddSingleton<IUIExtension>(new UIExtension("Monero/StoreWalletsNavMoneroExtension",  "store-wallets-nav"));
            serviceCollection.AddSingleton<ISyncSummaryProvider, MoneroSyncSummaryProvider>();

            return serviceCollection;
        }

        private static MoneroLikeConfiguration ConfigureMoneroLikeConfiguration(this IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
            var result = new MoneroLikeConfiguration();

            var supportedNetworks = btcPayNetworkProvider.GetAll()
                .OfType<MoneroLikeSpecificBtcPayNetwork>();

            foreach (var moneroLikeSpecificBtcPayNetwork in supportedNetworks)
            {
                var daemonUri =
                    configuration.GetOrDefault<Uri>($"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_uri",
                        null);
                var walletDaemonUri =
                    configuration.GetOrDefault<Uri>(
                        $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_uri", null);
                var walletDaemonWalletDirectory =
                    configuration.GetOrDefault<string>(
                        $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_walletdir", null);
                var daemonUsername =
                    configuration.GetOrDefault<string>(
                        $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_username", null);
                var daemonPassword =
                    configuration.GetOrDefault<string>(
                        $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_password", null);
                if (daemonUri == null || walletDaemonUri == null)
                {
                    throw new ConfigException($"{moneroLikeSpecificBtcPayNetwork.CryptoCode} is misconfigured");
                }

                result.MoneroLikeConfigurationItems.Add(moneroLikeSpecificBtcPayNetwork.CryptoCode, new MoneroLikeConfigurationItem()
                {
                    DaemonRpcUri = daemonUri,
                    Username = daemonUsername,
                    Password = daemonPassword,
                    InternalWalletRpcUri = walletDaemonUri,
                    WalletDirectory = walletDaemonWalletDirectory
                });
            }
            return result;
        }
    }
}
#endif
