using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Plugins.GlobalSearch.Views;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Bitcoin;

public class BitcoinLikeSearchResultProvider(
    BTCPayNetworkProvider networks,
    IStringLocalizer stringLocalizer,
    PrettyNameProvider prettyNameProvider) : ISearchResultItemProvider
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    public async Task ProvideAsync(SearchResultItemProviderContext context)
    {
        context.ItemResults.Add(new ResultItemViewModel()
        {
            Title = "On-chain wallets list",
            Category = "Wallets",
            Url = context.Url.Action(nameof(UIWalletsController.ListWallets), "UIWallets"),
            Keywords = ["List", "Wallets"]
        });
        var storeId = context.Store?.Id;
        if (storeId is null) return;

        if (!await context.IsAuthorized(Policies.CanModifyStoreSettings))
            return;
        foreach (var network in networks.GetAll().OfType<BTCPayNetwork>())
        {
            var walletId = new WalletId(storeId, network.CryptoCode);
            var translated = prettyNameProvider.PrettyName(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode), false);
            var untranslated = prettyNameProvider.PrettyName(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode), true);

            var prefix = StringLocalizer["Wallet ({0})", network.CryptoCode].Value + " ❯ ";

            if (!network.ReadonlyWallet)
                context.ItemResults.Add(new ResultItemViewModel()
                {
                    Title = prefix + StringLocalizer["Send"].Value,
                    Category = "Wallets",
                    Keywords = ["Send", "Wallets", network.CryptoCode, translated, untranslated]
                });
            context.ItemResults.Add(new ResultItemViewModel()
            {
                Title = prefix + StringLocalizer["Receive"].Value,
                Category = "Wallets",
                Url = context.Url.WalletReceive(walletId),
                Keywords = ["Receive", "Wallets", network.CryptoCode, translated, untranslated]
            });
            context.ItemResults.Add(new ResultItemViewModel()
            {
                Title = prefix + StringLocalizer["Transactions"].Value,
                Category = "Wallets",
                Url = context.Url.WalletTransactions(walletId),
                Keywords = ["Transactions", "Wallets", network.CryptoCode, translated, untranslated]
            });
            context.ItemResults.Add(new ResultItemViewModel()
            {
                Title = prefix + StringLocalizer["Settings"].Value,
                Category = "Wallets",
                Url = context.Url.WalletSettings(walletId),
                Keywords = ["Settings", "Wallets", network.CryptoCode, translated, untranslated]
            });
        }
    }
}
