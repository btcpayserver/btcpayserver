#nullable  enable
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins.Wallets;

public class HotwalletSafe(
    StoreRepository storeRepository,
    IAuthorizationService authorizationService,
    ExplorerClientProvider explorerClientProvider,
    PaymentMethodHandlerDictionary handlers)
{
    /// <summary>
    /// Represents the result of successfully unlocking a store hot wallet, including the wallet key material
    /// and the permissions granted to the current user.
    /// </summary>
    /// <param name="Store">The store that owns the wallet.</param>
    /// <param name="User">The user for whom the wallet was unlocked.</param>
    /// <param name="Settings">The derivation scheme settings associated with the wallet.</param>
    /// <param name="ExtKey">The master extended private key used by the hot wallet.</param>
    /// <param name="Mnemonic">The mnemonic phrase associated with the hot wallet, if available.</param>
    /// <param name="CanSign">Whether the user is authorized to sign wallet transactions.</param>
    /// <param name="CanSee">Whether the user is authorized to see the seed.</param>
    public record HotwalletRecord(StoreData Store, ClaimsPrincipal User, DerivationSchemeSettings Settings, ExtKey ExtKey, string? Mnemonic, bool CanSign, bool CanSee);
    public async Task<HotwalletRecord?> TryUnlock(ClaimsPrincipal user, WalletId walletId)
    {
        var store = await storeRepository.FindStore(walletId.StoreId, user);
        if (store is null)
            return null;
        var client = explorerClientProvider.GetExplorerClient(walletId.CryptoCode);
        var d = store.GetDerivationSchemeSettings(handlers, walletId.CryptoCode);
        if (d?.IsHotWallet is not true)
            return null;
        var metadata = await client.GetMetadataAsync<string>(d.AccountDerivation, WellknownMetadataKeys.MasterHDKey);
        if (string.IsNullOrWhiteSpace(metadata))
            return null;
        var mnemo = await client.GetMetadataAsync<string>(d.AccountDerivation, WellknownMetadataKeys.Mnemonic);
        return new(
            store,
            user,
            d,
            client.Network.NBitcoinNetwork.Parse<BitcoinExtKey>(metadata).ExtKey,
            mnemo,
            (await authorizationService.AuthorizeAsync(user, walletId.StoreId, WalletPolicies.CanSignWalletTransactions)).Succeeded,
            (await authorizationService.AuthorizeAsync(user, walletId.StoreId, Policies.CanModifyStoreSettings)).Succeeded
            );
    }
}
