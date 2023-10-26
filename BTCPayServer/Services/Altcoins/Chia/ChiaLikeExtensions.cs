#if ALTCOINS
using System;
using System.IO;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Common.Altcoins.Chia.RPC;
using BTCPayServer.Configuration;
using BTCPayServer.Payments;
using BTCPayServer.Services.Altcoins.Chia.Configuration;
using BTCPayServer.Services.Altcoins.Chia.Payments;
using BTCPayServer.Services.Altcoins.Chia.RPC;
using BTCPayServer.Services.Altcoins.Chia.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Services.Altcoins.Chia
{
    public static class ChiaLikeExtensions
    {
        public static IServiceCollection AddChiaLike(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton(provider =>
                provider.ConfigureChiaLikeConfiguration());
            serviceCollection.AddSingleton<ChiaRPCProvider>();
            serviceCollection.AddSingleton<ChiaLikePaymentHandler>();
            serviceCollection.AddHostedService<ChiaLikeSummaryUpdaterHostedService>();
            serviceCollection.AddHostedService<ChiaListener>();
            serviceCollection.AddHostedService<ChiaLikeTransactionUpdaterHostedService>();
            serviceCollection.AddSingleton<ChiaLikePaymentMethodHandler>();
            serviceCollection.AddSingleton<IPaymentMethodHandler>(provider =>
                provider.GetService<ChiaLikePaymentMethodHandler>());
            serviceCollection.AddSingleton<IUIExtension>(new UIExtension("Chia/StoreNavChiaExtension", "store-nav"));
            serviceCollection.AddSingleton<IUIExtension>(new UIExtension("Chia/StoreWalletsNavChiaExtension",  "store-wallets-nav"));
            serviceCollection.AddSingleton<ISyncSummaryProvider, ChiaSyncSummaryProvider>();

            return serviceCollection;
        }

        private static ChiaLikeConfiguration ConfigureChiaLikeConfiguration(this IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
            var result = new ChiaLikeConfiguration();

            var supportedChains = configuration.GetOrDefault<string>("chains", string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToUpperInvariant());

            var supportedNetworks = btcPayNetworkProvider.Filter(supportedChains.ToArray()).GetAll()
                .OfType<ChiaLikeSpecificBtcPayNetwork>();

            foreach (var ChiaLikeSpecificBtcPayNetwork in supportedNetworks)
            {
                var chiaRoot =
                    configuration.GetOrDefault<Uri>($"{ChiaLikeSpecificBtcPayNetwork.CryptoCode}_root",
                        null);
                if (chiaRoot == null)
                {
                    throw new ConfigException($"{ChiaLikeSpecificBtcPayNetwork.CryptoCode} is misconfigured");
                }

                var config = Config.Open(Path.Combine(chiaRoot.AbsolutePath, "config", "config.yaml"));

                result.ChiaLikeConfigurationItems.Add(ChiaLikeSpecificBtcPayNetwork.CryptoCode,
                    new ChiaLikeConfigurationItem()
                    {
                        FullNodeEndpoint = config.GetEndpoint("full_node"),
                        WalletEndpoint = config.GetEndpoint("wallet"),
                    });
            }

            return result;
        }
    }
}
#endif
