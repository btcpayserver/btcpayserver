#nullable enable

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Plugins.GlobalSearch.Views;
using BTCPayServer.Plugins.Multisig.Controllers;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Multisig;

public class MultisigSearchResultProvider(
    PaymentMethodHandlerDictionary handlers,
    PrettyNameProvider prettyNameProvider,
    MultisigService multisigService,
    IStringLocalizer stringLocalizer) : ISearchResultItemProvider
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    public async Task ProvideAsync(SearchResultItemProviderContext context, CancellationToken cancellationToken)
    {
        if (context.UserQuery is not null || context.Store is not { Id: var storeId })
            return;

        var pendingsByCryptoCode = (await multisigService.GetPendingMultisigSetup(storeId)).ToDictionary(o => o.CryptoCode);
        foreach (var network in handlers.OfType<BitcoinLikePaymentHandler>().Select(h => h.Network))
        {
            var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var translated = prettyNameProvider.PrettyName(paymentMethodId, false);
            var untranslated = prettyNameProvider.PrettyName(paymentMethodId, true);
            var category = StringLocalizer["On-chain wallets", network.CryptoCode].Value + " ❯ " + translated;
            var hasWallet = context.Store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, handlers) is not null;
            if (pendingsByCryptoCode.TryGetValue(network.CryptoCode, out var pending) &&
                (!hasWallet || pending.ReplacesExistingWallet))
            {
                var setupAccess = await context.AuthorizationService.GetSetupAccess(storeId, context.User, pending);
                if (!setupAccess.CanViewStatus)
                    continue;

                context.ItemResults.Add(new ResultItemViewModel
                {
                    Title = StringLocalizer["Continue multisig setup in progress"].Value,
                    Url = context.Url.Action(nameof(UIMultisigSetupController.SetupMultisigStatus), "UIMultisigSetup", new { area = MultisigPlugin.Area, multisigSetupId = pending.RequestId }),
                    Category = category,
                    Keywords = ["Multisig", "Setup", "Wallet", network.CryptoCode, translated, untranslated],
                    Order = 1
                });
            }

            if (!hasWallet)
            {
                context.ItemResults.Add(new ResultItemViewModel
                {
                    RequiredPolicy = WalletPolicies.CanManageWalletSettings,
                    Title = StringLocalizer["Set up a multisig wallet"].Value,
                    Url = context.Url.Action(nameof(UIMultisigWalletsController.SetupMultisig), "UIMultisigWallets", new { area = MultisigPlugin.Area, storeId, cryptoCode = network.CryptoCode }),
                    Category = category,
                    Keywords = ["Multisig", "Setup", "Wallet", network.CryptoCode, translated, untranslated],
                    Order = 1
                });
            }
        }
    }
}
