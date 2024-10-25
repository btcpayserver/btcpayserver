using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.PayoutProcessors.Lightning;
using BTCPayServer.PayoutProcessors.OnChain;
using BTCPayServer.Payouts;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.PayoutProcessors;

public static class PayoutProcessorsExtensions
{
    public static void AddPayoutProcesors(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<OnChainAutomatedPayoutSenderFactory>();
        serviceCollection.AddSingleton<IPayoutProcessorFactory>(provider => provider.GetRequiredService<OnChainAutomatedPayoutSenderFactory>());
        serviceCollection.AddSingleton<LightningAutomatedPayoutSenderFactory>();
        serviceCollection.AddSingleton<IPayoutProcessorFactory>(provider => provider.GetRequiredService<LightningAutomatedPayoutSenderFactory>());
        serviceCollection.AddSingleton<PayoutProcessorService>();
        serviceCollection.AddHostedService(s => s.GetRequiredService<PayoutProcessorService>());
    }

    public static PayoutMethodId GetPayoutMethodId(this PayoutProcessorData data)
    {
        return PayoutMethodId.Parse(data.PayoutMethodId);
    }
}
