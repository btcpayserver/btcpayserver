#nullable enable

using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Plugins.GlobalSearch.Views;
using BTCPayServer.Plugins.Multisig.Controllers;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Multisig;

public class MultisigSearchResultProvider(
    PaymentMethodHandlerDictionary handlers,
    PrettyNameProvider prettyNameProvider,
    StoreRepository storeRepository,
    IStringLocalizer stringLocalizer) : ISearchResultItemProvider
{
    public async Task ProvideAsync(SearchResultItemProviderContext context, CancellationToken cancellationToken)
    {
        if (context.UserQuery is not null || context.Store is not { Id: var storeId })
            return;

        const string cryptoCode = "BTC";
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
        if (!handlers.Support(paymentMethodId))
            return;

        var translated = prettyNameProvider.PrettyName(paymentMethodId, false);
        var untranslated = prettyNameProvider.PrettyName(paymentMethodId, true);
        var category = stringLocalizer["On-chain wallets", cryptoCode].Value + " ❯ " + translated;
        var pending = await storeRepository.GetSettingAsync<Models.PendingMultisigSetupData>(storeId, MultisigService.GetPendingMultisigSettingName(cryptoCode));
        var hasWallet = context.Store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, handlers) is not null;
        if (pending is not null && pending.ExpiresAt >= System.DateTimeOffset.UtcNow)
        {
            if (hasWallet && !pending.ReplacesExistingWallet)
                return;

            var setupAccess = await context.AuthorizationService.GetSetupAccess(storeId, context.User, pending);
            if (!setupAccess.CanViewStatus)
                return;

            context.ItemResults.Add(new ResultItemViewModel
            {
                Title = stringLocalizer["Multisig setup in progress"].Value,
                Url = context.Url.Action(nameof(UIMultisigStatusController.Status), "UIMultisigStatus", new { area = MultisigPlugin.Area, multisigSetupId = pending.RequestId }),
                Category = category,
                Keywords = ["Multisig", "Setup multisig", "Setup status", "Signer request", "Submit signer key", "Signer", "Signers", "XPUB", "Vault", "Hardware wallet", cryptoCode, translated, untranslated]
            });
            return;
        }
        if (hasWallet)
            return;

        context.ItemResults.Add(new ResultItemViewModel
        {
            RequiredPolicy = WalletPolicies.CanManageWalletSettings,
            Title = stringLocalizer["Set up a multisig wallet"].Value,
            Url = context.Url.Action(nameof(UIMultisigSetupController.SetupMultisig), "UIMultisigSetup", new { area = MultisigPlugin.Area, storeId, cryptoCode }),
            Category = category,
            Keywords = ["Multisig", "Setup multisig", "Signer request", "Submit signer key", "Signer", "Signers", "XPUB", "Vault", "Hardware wallet", cryptoCode, translated, untranslated]
        });
    }
}
