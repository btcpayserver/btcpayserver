using BTCPayServer.BIP78.Sender;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments.PayJoin.Sender;
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
            services.AddSingleton<UTXOLocker>();
            services.AddSingleton<IUTXOLocker>(provider => provider.GetRequiredService<UTXOLocker>());
            services.AddSingleton<IPayjoinServerCommunicator, PayjoinServerCommunicator>();
            services.AddSingleton<PayjoinClient>();
            services.AddTransient<Socks5HttpClientHandler>();
            services.AddHttpClient(PayjoinServerCommunicator.PayjoinOnionNamedClient)
                .ConfigurePrimaryHttpMessageHandler<Socks5HttpClientHandler>();
        }
    }
}
