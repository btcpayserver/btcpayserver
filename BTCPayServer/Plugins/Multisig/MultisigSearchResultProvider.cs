#nullable enable

using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Plugins.GlobalSearch.Views;
using BTCPayServer.Plugins.Multisig.Controllers;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Multisig;

public class MultisigSearchResultProvider(
    PaymentMethodHandlerDictionary handlers,
    PrettyNameProvider prettyNameProvider,
    IStringLocalizer stringLocalizer) : ISearchResultItemProvider
{
    public Task ProvideAsync(SearchResultItemProviderContext context, CancellationToken cancellationToken)
    {
        if (context.UserQuery is not null || context.Store is not { Id: var storeId })
            return Task.CompletedTask;

        const string cryptoCode = "BTC";
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
        if (!handlers.Support(paymentMethodId) ||
            context.Store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, handlers) is not null)
            return Task.CompletedTask;

        var translated = prettyNameProvider.PrettyName(paymentMethodId, false);
        var untranslated = prettyNameProvider.PrettyName(paymentMethodId, true);
        var category = stringLocalizer["On-chain wallets", cryptoCode].Value + " ❯ " + translated;

        context.ItemResults.Add(new ResultItemViewModel
        {
            RequiredPolicy = WalletPolicies.CanManageWalletSettings,
            Title = "Set up a multisig wallet",
            Url = context.Url.Action(nameof(UIMultisigSetupController.SetupMultisig), "UIMultisigSetup", new { area = MultisigPlugin.Area, storeId, cryptoCode }),
            Category = category,
            Keywords = ["Multisig", "Setup multisig", "Invite", "Invite signer", "Signer", "Signers", "XPUB", "Vault", "Hardware wallet", cryptoCode, translated, untranslated]
        });

        return Task.CompletedTask;
    }
}
