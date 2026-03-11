#nullable enable

using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.DataProtection;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Wallets;

public class OnChainWalletSetupService(
    BTCPayWalletProvider walletProvider,
    PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
    StoreRepository storeRepository,
    EventAggregator eventAggregator,
    IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector _dataProtector = dataProtectionProvider.CreateProtector("ConfigProtector");

    public string ProtectConfig(string cryptoCode, DerivationSchemeSettings derivationSchemeSettings)
    {
        var handler = paymentMethodHandlerDictionary.GetBitcoinHandler(cryptoCode);
        return _dataProtector.ProtectString(JToken.FromObject(derivationSchemeSettings, handler.Serializer).ToString());
    }

    public bool TryParseProtectedConfig(string cryptoCode, string protectedConfig, out DerivationSchemeSettings? derivationSchemeSettings)
    {
        derivationSchemeSettings = null;
        if (string.IsNullOrWhiteSpace(protectedConfig))
            return false;

        try
        {
            var handler = paymentMethodHandlerDictionary.GetBitcoinHandler(cryptoCode);
            derivationSchemeSettings = handler.ParsePaymentMethodConfig(JToken.Parse(_dataProtector.UnprotectString(protectedConfig)));
            return true;
        }
        catch
        {
            return false;
        }
    }

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
            await storeRepository.UpdateStore(store);
            eventAggregator.Publish(new WalletChangedEvent { WalletId = new WalletId(store.Id, network.CryptoCode) });
            return new OnChainWalletSetupResult(true, null);
        }
        catch
        {
            return new OnChainWalletSetupResult(false, "NBXplorer is unable to track this derivation scheme. You may need to update it.");
        }
    }
}

public record OnChainWalletSetupResult(bool Success, string? ErrorMessage);
