#nullable enable

using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Multisig.HostedServices;

public class MultisigPendingTransactionHostedService(
    EventAggregator eventAggregator,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MultisigPendingTransactionHostedService> logger)
    : EventHostedServiceBase(eventAggregator, logger)
{
    protected override void SubscribeToEvents()
    {
        Subscribe<PendingTransactionService.PendingTransactionEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is not PendingTransactionService.PendingTransactionEvent pendingEvent ||
            pendingEvent.Type is not PendingTransactionService.PendingTransactionEvent.Created and not PendingTransactionService.PendingTransactionEvent.SignatureCollected)
            return;

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var storeRepository = scope.ServiceProvider.GetRequiredService<StoreRepository>();
        var handlers = scope.ServiceProvider.GetRequiredService<PaymentMethodHandlerDictionary>();
        var multisigNotificationService = scope.ServiceProvider.GetRequiredService<MultisigNotificationService>();

        var pendingTransaction = pendingEvent.Data;
        var store = await storeRepository.FindStore(pendingTransaction.StoreId);
        if (store is null)
            return;

        var walletId = new WalletId(pendingTransaction.StoreId, pendingTransaction.CryptoCode);
        var derivation = store.GetDerivationSchemeSettings(handlers, walletId.CryptoCode);
        if (derivation is null)
            return;

        switch (pendingEvent.Type)
        {
            case PendingTransactionService.PendingTransactionEvent.Created:
                await multisigNotificationService.NotifyPendingTransactionCreated(walletId, pendingTransaction, derivation);
                break;
            case PendingTransactionService.PendingTransactionEvent.SignatureCollected:
                await multisigNotificationService.NotifyPendingTransactionSignatureCollected(walletId, pendingTransaction, derivation, pendingEvent.SignerUserId);
                break;
        }
    }
}
