using System;
using System.Linq;
using BTCPayServer.Configuration;
using BTCPayServer.Contracts;
using BTCPayServer.Services.Altcoins.Monero.Configuration;
using BTCPayServer.Services.Altcoins.Monero.Payments;
using BTCPayServer.Services.Altcoins.Monero.Services;
using BTCPayServer.Payments;
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
            serviceCollection.AddSingleton<MoneroRPCProvider>();
            serviceCollection.AddHostedService<MoneroLikeSummaryUpdaterHostedService>();
            serviceCollection.AddHostedService<MoneroListener>();
            serviceCollection.AddSingleton<MoneroLikePaymentMethodHandler>();
            serviceCollection.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<MoneroLikePaymentMethodHandler>());
            serviceCollection.AddSingleton<IStoreNavExtension,MoneroStoreNavExtension>();
            
            return serviceCollection;
        }
        
        private  static MoneroLikeConfiguration ConfigureMoneroLikeConfiguration(this IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
            var result = new MoneroLikeConfiguration();

            var supportedChains = configuration.GetOrDefault<string>("chains", string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToUpperInvariant());

            var supportedNetworks = btcPayNetworkProvider.Filter(supportedChains.ToArray()).GetAll()
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
                if (daemonUri == null || walletDaemonUri == null )
                {
                    throw new ConfigException($"{moneroLikeSpecificBtcPayNetwork.CryptoCode} is misconfigured");
                }
                
                result.MoneroLikeConfigurationItems.Add(moneroLikeSpecificBtcPayNetwork.CryptoCode, new MoneroLikeConfigurationItem()
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
