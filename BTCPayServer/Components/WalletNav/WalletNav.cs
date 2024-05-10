#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NBitcoin;
using NBitcoin.Secp256k1;

namespace BTCPayServer.Components.WalletNav
{
    public class WalletNav : ViewComponent
    {
        private readonly BTCPayWalletProvider _walletProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly UIWalletsController _walletsController;
        private readonly CurrencyNameTable _currencies;
        private readonly DefaultRulesCollection _defaultRules;
        private readonly RateFetcher _rateFetcher;

        public WalletNav(
            BTCPayWalletProvider walletProvider,
            PaymentMethodHandlerDictionary handlers,
            UIWalletsController walletsController,
            CurrencyNameTable currencies,
            DefaultRulesCollection defaultRules,
            RateFetcher rateFetcher)
        {
            _walletProvider = walletProvider;
            _handlers = handlers;
            _walletsController = walletsController;
            _currencies = currencies;
            _defaultRules = defaultRules;
            _rateFetcher = rateFetcher;
        }

        public async Task<IViewComponentResult> InvokeAsync(WalletId walletId)
        {
            var store = ViewContext.HttpContext.GetStoreData();
            var network = _handlers.TryGetNetwork(PaymentTypes.CHAIN.GetPaymentMethodId(walletId.CryptoCode));
            if (network is null)
                return new HtmlContentViewComponentResult(new StringHtmlContent(string.Empty));
            var wallet = _walletProvider.GetWallet(network);
            var defaultCurrency = store.GetStoreBlob().DefaultCurrency;
            var derivation = store.GetDerivationSchemeSettings(_handlers, walletId.CryptoCode);
            var balance = await wallet.GetBalance(derivation?.AccountDerivation) switch
            {
                { Available: null, Total: var total } => total,
                { Available: var available } => available
            };

            var vm = new WalletNavViewModel
            {
                WalletId = walletId,
                Network = network,
                Balance = balance.ShowMoney(network),
                DefaultCurrency = defaultCurrency,
                Label = derivation?.Label ?? $"{store.StoreName} {walletId.CryptoCode} Wallet"
            };

            if (defaultCurrency != network.CryptoCode)
            {
                var rule = store.GetStoreBlob().GetRateRules(_defaultRules)?.GetRuleFor(new Rating.CurrencyPair(network.CryptoCode, defaultCurrency));
                var bid = rule is null ? null : (await _rateFetcher.FetchRate(rule, new StoreIdRateContext(walletId.StoreId), HttpContext.RequestAborted)).BidAsk?.Bid;
                if (bid is decimal b)
                {
                    var currencyData = _currencies.GetCurrencyData(defaultCurrency, true);
                    vm.BalanceDefaultCurrency = (balance.GetValue(network) * b).ShowMoney(currencyData.Divisibility);
                }
            }

            return View(vm);
        }
    }
}
