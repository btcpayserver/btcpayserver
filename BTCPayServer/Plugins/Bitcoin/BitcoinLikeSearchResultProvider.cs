using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Plugins.GlobalSearch.Views;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Bitcoin;

public class BitcoinLikeSearchResultProvider(
    BTCPayNetworkProvider networks,
    IStringLocalizer stringLocalizer,
    PaymentMethodHandlerDictionary paymentMethodHandlers,
    PrettyNameProvider prettyNameProvider) : ISearchResultItemProvider
{
    private const string Category = "Wallets";
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    public Task ProvideAsync(SearchResultItemProviderContext context, CancellationToken cancellationToken)
    {
        if (context.UserQuery is not null)
            return Task.CompletedTask;

        if (context.Store is null)
            return Task.CompletedTask;

        context.ItemResults.Add(new ResultItemViewModel()
        {
            Title = "On-chain wallets list",
            Category = Category,
            Url = context.Url.Action(nameof(UIWalletsController.ListWallets), "UIWallets"),
            Keywords = ["List", "Wallets"],
            RequiredPolicy = Policies.CanModifyStoreSettings
        });
        var storeId = context.Store?.Id;
        if (storeId is null) return Task.CompletedTask;

        foreach (var network in networks.GetAll().OfType<BTCPayNetwork>())
        {
            var walletId = new WalletId(storeId, network.CryptoCode);


            if (paymentMethodHandlers.Support(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode)))
            {
                var prefix = StringLocalizer["Wallet ({0})", network.CryptoCode].Value + " ❯ ";
                var translated = prettyNameProvider.PrettyName(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode), false);
                var untranslated = prettyNameProvider.PrettyName(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode), true);

                var settings = context.Store.GetDerivationSchemeSettings(paymentMethodHandlers, network.CryptoCode);
                if (settings is null)
                {
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = prefix + StringLocalizer["Set up a wallet"].Value,
                        Category = Category,
                        Url = context.Url.Action(nameof(UIStoresController.SetupWallet), "UIStores", new { storeId, cryptoCode = network.CryptoCode }),
                        Keywords = ["Setup", "Wallets", network.CryptoCode, translated, untranslated],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                }
                else
                {
                    if (!network.ReadonlyWallet)
                        context.ItemResults.Add(new ResultItemViewModel()
                        {
                            Title = prefix + StringLocalizer["Send"].Value,
                            Category = Category,
                            Keywords = ["Send", "Wallets", network.CryptoCode, translated, untranslated],
                            RequiredPolicy = Policies.CanModifyStoreSettings
                        });
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = prefix + StringLocalizer["Receive"].Value,
                        Category = Category,
                        Url = context.Url.WalletReceive(walletId),
                        Keywords = ["Receive", "Wallets", network.CryptoCode, translated, untranslated],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = prefix + StringLocalizer["Transactions"].Value,
                        Category = Category,
                        Url = context.Url.WalletTransactions(walletId),
                        Keywords = ["Transactions", "Wallets", network.CryptoCode, translated, untranslated],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = prefix + StringLocalizer["Settings"].Value,
                        Category = Category,
                        Url = context.Url.WalletSettings(walletId),
                        Keywords = ["Settings", "Wallets", network.CryptoCode, translated, untranslated],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                }
            }

            if (paymentMethodHandlers.Support(PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode)))
            {
                var prefix = StringLocalizer["Lightning ({0})", network.CryptoCode].Value + " ❯ ";
                var translated = prettyNameProvider.PrettyName(PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode), false);
                var untranslated = prettyNameProvider.PrettyName(PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode), true);

                var settings = paymentMethodHandlers.GetLightningConfig(context.Store, network);
                if (settings is null)
                {
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = prefix + StringLocalizer["Set up a Lightning node"].Value,
                        Category = Category,
                        Url = context.Url.Action(nameof(UIStoresController.SetupWallet), "UIStores", new { storeId, cryptoCode = network.CryptoCode }),
                        Keywords = ["Setup", "Wallets", network.CryptoCode, translated, untranslated],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                }
                else
                {
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = prefix + StringLocalizer["Public Node Info"].Value,
                        Category = Category,
                        Url = context.Url.Action(nameof(UIPublicLightningNodeInfoController.ShowLightningNodeInfo), "UIPublicLightningNodeInfo", new { storeId, cryptoCode = network.CryptoCode }),
                        Keywords = [translated, untranslated],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                }
            }
        }
        return Task.CompletedTask;
    }
}
