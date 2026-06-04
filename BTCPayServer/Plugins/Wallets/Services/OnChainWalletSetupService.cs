#nullable enable

using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.DataProtection;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Wallets.Services;

public class OnChainWalletSetupService(
    BTCPayWalletProvider walletProvider,
    PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
    StoreRepository storeRepository,
    EventAggregator eventAggregator)
{
    public async Task<OnChainWalletSetupResult> SaveWallet(StoreData store, BTCPayNetwork network, DerivationSchemeSettings derivationSchemeSettings, WalletSetupRequest? setupRequest)
    {
        var wallet = walletProvider.GetWallet(network);
        if (wallet is null)
            return new OnChainWalletSetupResult(false, "Wallet is not available.");

        try
        {
            await wallet.TrackAsync(derivationSchemeSettings.AccountDerivation);
            var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            store.SetPaymentMethodConfig(paymentMethodHandlerDictionary[paymentMethodId], derivationSchemeSettings);
            var storeBlob = store.GetStoreBlob();
            storeBlob.SetExcluded(paymentMethodId, false);
            storeBlob.PayJoinEnabled = derivationSchemeSettings.IsHotWallet && !(setupRequest?.PayJoinEnabled is false);
            store.SetStoreBlob(storeBlob);
        }
        catch
        {
            return new OnChainWalletSetupResult(false, "NBXplorer is unable to track this derivation scheme. You may need to update it.");
        }

        await storeRepository.UpdateStore(store);
        eventAggregator.Publish(new WalletChangedEvent { WalletId = new WalletId(store.Id, network.CryptoCode) });
        return new OnChainWalletSetupResult(true, null);
    }
}

public record OnChainWalletSetupResult(bool Success, string? ErrorMessage);
