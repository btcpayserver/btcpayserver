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
    private const string OnChainCategory = "On-chain wallets";
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    public Task ProvideAsync(SearchResultItemProviderContext context, CancellationToken cancellationToken)
    {
        if (context.UserQuery is not null)
            return Task.CompletedTask;

        if (context.Store is null)
            return Task.CompletedTask;

        context.ItemResults.Add(new ResultItemViewModel()
        {
            Title = "List wallets",
            Category = OnChainCategory,
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

                var translated = prettyNameProvider.PrettyName(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode), false);
                var category = StringLocalizer["On-chain wallets", network.CryptoCode].Value + " ❯ " + translated;
                var untranslated = prettyNameProvider.PrettyName(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode), true);

                var settings = context.Store.GetDerivationSchemeSettings(paymentMethodHandlers, network.CryptoCode);
                if (settings is null)
                {
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = StringLocalizer["Set up a wallet"].Value,
                        Category = category,
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
                            Title = StringLocalizer["Create a new transaction"].Value,
                            Category = category,
                            Keywords = ["Send", "Wallets", "Create", "transaction", network.CryptoCode, translated, untranslated],
                            RequiredPolicy = Policies.CanModifyStoreSettings
                        });
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = StringLocalizer["Get a deposit address"].Value,
                        Category = category,
                        Url = context.Url.WalletReceive(walletId),
                        Keywords = ["Receive", "Deposit", "Address", "Wallets", network.CryptoCode, translated, untranslated],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = StringLocalizer["Browse all deposit addresses"].Value,
                        Category = category,
                        Url = context.Url.WalletReceive(walletId),
                        Keywords = ["Receive", "Deposit", "Address", "Wallets", network.CryptoCode, translated, untranslated],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = StringLocalizer["View transactions"].Value,
                        Category = category,
                        Url = context.Url.WalletTransactions(walletId),
                        Keywords = ["Transactions", "View", "Wallets", network.CryptoCode, translated, untranslated],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = StringLocalizer["Go to wallet's settings"].Value,
                        Category = category,
                        Url = context.Url.WalletSettings(walletId),
                        Keywords = ["Settings", "Wallets", network.CryptoCode, translated, untranslated],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                }
            }

            if (paymentMethodHandlers.Support(PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode)))
            {
                var translated = prettyNameProvider.PrettyName(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode), false);
                var category = StringLocalizer["Lightning", network.CryptoCode].Value + " ❯ " + translated;
                var untranslated = prettyNameProvider.PrettyName(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode), true);

                var lntranslated = prettyNameProvider.PrettyName(PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode), false);
                var lnuntranslated = prettyNameProvider.PrettyName(PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode), true);

                var settings = paymentMethodHandlers.GetLightningConfig(context.Store, network);
                if (settings is null)
                {
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = StringLocalizer["Set up a Lightning node"].Value,
                        Category = category,
                        Url = context.Url.Action(nameof(UIStoresController.SetupLightningNode), "UIStores", new { storeId, cryptoCode = network.CryptoCode }),
                        Keywords = ["Setup", "Wallets", network.CryptoCode, translated, untranslated, lntranslated, lnuntranslated, "Lightning"],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                }
                else
                {
                    context.ItemResults.Add(new ResultItemViewModel()
                    {
                        Title = StringLocalizer["View the public node info"].Value,
                        Category = category,
                        Url = context.Url.Action(nameof(UIPublicLightningNodeInfoController.ShowLightningNodeInfo), "UIPublicLightningNodeInfo", new { storeId, cryptoCode = network.CryptoCode }),
                        Keywords = ["Public", "Node", "Info", "View", translated, untranslated, lntranslated, lnuntranslated, "Lightning"],
                        RequiredPolicy = Policies.CanModifyStoreSettings
                    });
                }
            }
        }
        return Task.CompletedTask;
    }
}
