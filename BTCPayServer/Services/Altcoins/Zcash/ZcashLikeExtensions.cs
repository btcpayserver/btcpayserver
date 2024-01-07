#if ALTCOINS
using System;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Configuration;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Services.Altcoins.Zcash.Configuration;
using BTCPayServer.Services.Altcoins.Zcash.Payments;
using BTCPayServer.Services.Altcoins.Zcash.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Services.Altcoins.Zcash
{
    public static class ZcashLikeExtensions
    {
        public static IServiceCollection AddZcashLike(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton(provider =>
                provider.ConfigureZcashLikeConfiguration());
            serviceCollection.AddSingleton<ZcashRPCProvider>();
            serviceCollection.AddHostedService<ZcashLikeSummaryUpdaterHostedService>();
            serviceCollection.AddHostedService<ZcashListener>();
            serviceCollection.AddSingleton<ZcashLikePaymentMethodHandler>();
            serviceCollection.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<ZcashLikePaymentMethodHandler>());
            serviceCollection.AddSingleton<IUIExtension>(new UIExtension("Zcash/StoreNavZcashExtension",  "store-nav"));
            serviceCollection.AddSingleton<ISyncSummaryProvider, ZcashSyncSummaryProvider>();

            return serviceCollection;
        }

        private static ZcashLikeConfiguration ConfigureZcashLikeConfiguration(this IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
            var result = new ZcashLikeConfiguration();

            var supportedNetworks = btcPayNetworkProvider.GetAll()
                .OfType<ZcashLikeSpecificBtcPayNetwork>();

            foreach (var ZcashLikeSpecificBtcPayNetwork in supportedNetworks)
            {
                var daemonUri =
                    configuration.GetOrDefault<Uri>($"{ZcashLikeSpecificBtcPayNetwork.CryptoCode}_daemon_uri",
                        null);
                var walletDaemonUri =
                    configuration.GetOrDefault<Uri>(
                        $"{ZcashLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_uri", null);
                var walletDaemonWalletDirectory =
                    configuration.GetOrDefault<string>(
                        $"{ZcashLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_walletdir", null);
                if (daemonUri == null || walletDaemonUri == null)
                {
                    throw new ConfigException($"{ZcashLikeSpecificBtcPayNetwork.CryptoCode} is misconfigured");
                }

                result.ZcashLikeConfigurationItems.Add(ZcashLikeSpecificBtcPayNetwork.CryptoCode, new ZcashLikeConfigurationItem()
                {
                    DaemonRpcUri = daemonUri,
                    InternalWalletRpcUri = walletDaemonUri,
                    WalletDirectory = walletDaemonWalletDirectory
                });
            }
            return result;
        }
    }
}
#endif
