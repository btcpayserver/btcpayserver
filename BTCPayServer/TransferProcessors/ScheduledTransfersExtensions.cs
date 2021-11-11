using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Payments;
using BTCPayServer.TransferProcessors.Lightning;
using BTCPayServer.TransferProcessors.OnChain;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.TransferProcessors;

public static class ScheduledTransfersExtensions
{
    public static void AddScheduledTransfers(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<OnChainAutomatedTransferSenderFactory>();
        serviceCollection.AddSingleton<ITransferProcessorFactory>(provider => provider.GetRequiredService<OnChainAutomatedTransferSenderFactory>());
        serviceCollection.AddSingleton<LightningAutomatedTransferSenderFactory>();
        serviceCollection.AddSingleton<ITransferProcessorFactory>(provider => provider.GetRequiredService<LightningAutomatedTransferSenderFactory>());
        serviceCollection.AddHostedService<TransferProcessorService>();
        serviceCollection.AddSingleton<TransferProcessorService>();
        serviceCollection.AddHostedService(s=> s.GetRequiredService<TransferProcessorService>());
    }

    public static PaymentMethodId GetPaymentMethodId(this TransferProcessorData data)
    {
        return PaymentMethodId.Parse(data.PaymentMethod);
    }
}
