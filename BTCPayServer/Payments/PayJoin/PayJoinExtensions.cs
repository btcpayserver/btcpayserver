using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Payments.PayJoin
{
    public static class PayJoinExtensions
    {
        public static void AddPayJoinServices(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<PayJoinStateProvider>();
            serviceCollection.AddHostedService<PayJoinTransactionBroadcaster>();
        }
    }
}
