using System;
using System.Linq;
using BTCPayServer.Configuration;
using BTCPayServer.Payments.Monero;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Monero
{
    public static class MoneroLikeExtensions
    {
        public static IServiceCollection AddMoneroLike(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<MoneroLikeConfiguration>(provider =>
                provider.ConfigureMoneroLikeConfiguration());
            return serviceCollection;
        }

        public static IApplicationBuilder UseMoneroLike(this IApplicationBuilder applicationBuilder)
        {
            return applicationBuilder;
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
                    configuration.GetOrDefault<Uri>($"{moneroLikeSpecificBtcPayNetwork.CryptoCode}.daemon.uri",
                        null);
                var walletDaemonUri =
                    configuration.GetOrDefault<Uri>(
                        $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}.wallet.daemon.uri", null);
                if (daemonUri == null || walletDaemonUri == null )
                {
                    throw new ConfigException($"{moneroLikeSpecificBtcPayNetwork.CryptoCode} is misconfigured");
                }
                
                result.MoneroLikeConfigurationItems.Add(moneroLikeSpecificBtcPayNetwork.CryptoCode, new MoneroLikeConfigurationItem()
                {
                    DaemonRpcUri = daemonUri,
                    InternalWalletRpcUri = walletDaemonUri
                });
            }
            return result;
        }
    }
    
    
}
