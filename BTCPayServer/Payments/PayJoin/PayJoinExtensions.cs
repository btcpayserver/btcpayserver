using BTCPayServer.HostedServices;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Payments.PayJoin
{
    public static class PayJoinExtensions
    {
        public static void AddPayJoinServices(this IServiceCollection services)
        {
            services.AddSingleton<DelayedTransactionBroadcaster>();
            services.AddSingleton<IHostedService, HostedServices.DelayedTransactionBroadcasterHostedService>();
            services.AddSingleton<HostedServices.Socks5HttpProxyServer>();
            services.AddSingleton<IHostedService, HostedServices.Socks5HttpProxyServer>(s => s.GetRequiredService<Socks5HttpProxyServer>());
            services.AddSingleton<PayJoinRepository>();
            services.AddSingleton<PayjoinClient>();
            services.AddTransient<Socks5HttpClientHandler>();
            services.AddHttpClient(PayjoinClient.PayjoinOnionNamedClient)
                .ConfigureHttpClient(h => h.DefaultRequestHeaders.ConnectionClose = true)
                .ConfigurePrimaryHttpMessageHandler<Socks5HttpClientHandler>();
            services.AddHttpClient(WebhookNotificationManager.OnionNamedClient)
                .ConfigureHttpClient(h => h.DefaultRequestHeaders.ConnectionClose = true)
                .ConfigurePrimaryHttpMessageHandler<Socks5HttpClientHandler>();
        }
    }
}
