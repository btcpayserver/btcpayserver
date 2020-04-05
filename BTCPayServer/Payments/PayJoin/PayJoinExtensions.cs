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
            services.AddSingleton<PayJoinRepository>();
            services.AddSingleton<PayjoinClient>();
        }
    }
}
