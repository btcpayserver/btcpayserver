#if ALTCOINS
using System.Net;
using System.Net.Http;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Services.Altcoins.Ethereum.Payments;
using BTCPayServer.Services.Altcoins.Ethereum.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Services.Altcoins.Ethereum
{
    public static class EthereumLikeExtensions
    {
        public  const string EthereumInvoiceCheckHttpClient = "EthereumCheck";
        public  const string EthereumInvoiceCreateHttpClient = "EthereumCreate";
        public static IServiceCollection AddEthereumLike(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<EthereumService>();
            serviceCollection.AddSingleton<IHostedService, EthereumService>(provider => provider.GetService<EthereumService>());
            serviceCollection.AddSingleton<EthereumLikePaymentMethodHandler>();
            serviceCollection.AddSingleton<IPaymentMethodHandler>(provider => provider.GetService<EthereumLikePaymentMethodHandler>());
            
            serviceCollection.AddSingleton<IUIExtension>(new UIExtension("Ethereum/StoreNavEthereumExtension",  "store-nav"));
            serviceCollection.AddTransient<NoRedirectHttpClientHandler>();
            serviceCollection.AddSingleton<ISyncSummaryProvider, EthereumSyncSummaryProvider>();
            serviceCollection.AddHttpClient(EthereumInvoiceCreateHttpClient)
                .ConfigurePrimaryHttpMessageHandler<NoRedirectHttpClientHandler>();
            return serviceCollection;
        }
    }
    
    public class NoRedirectHttpClientHandler : HttpClientHandler
    {
        public NoRedirectHttpClientHandler()
        {
            this.AllowAutoRedirect = false;
        }
    }
}
#endif
