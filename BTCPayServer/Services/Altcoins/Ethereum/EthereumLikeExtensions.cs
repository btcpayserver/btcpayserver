#if ALTCOINS_RELEASE || DEBUG
using BTCPayServer.Contracts;
using BTCPayServer.Payments;
using BTCPayServer.Services.Altcoins.Ethereum.Payments;
using BTCPayServer.Services.Altcoins.Ethereum.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Services.Altcoins.Ethereum
{
    public static class EthereumLikeExtensions
    {
        public static IServiceCollection AddEthereumLike(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<EthereumService>();
            serviceCollection.AddSingleton<IHostedService, EthereumService>(provider => provider.GetService<EthereumService>());
            serviceCollection.AddSingleton<EthereumLikePaymentMethodHandler>();
            serviceCollection.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<EthereumLikePaymentMethodHandler>());
            serviceCollection.AddSingleton<IStoreNavExtension, EthereumStoreNavExtension>();
            serviceCollection.AddSingleton<ISyncSummaryProvider, EthereumSyncSummaryProvider>();
            return serviceCollection;
        }
    }
}
#endif
