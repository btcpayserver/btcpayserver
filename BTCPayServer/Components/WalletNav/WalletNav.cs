#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
using NBitcoin;
using NBitcoin.Secp256k1;

namespace BTCPayServer.Components.WalletNav
{
    public class WalletNav : ViewComponent
    {
        private readonly BTCPayWalletProvider _walletProvider;
        private readonly UIWalletsController _walletsController;
        private readonly CurrencyNameTable _currencies;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly IRateProvider _rateProvider;

        public WalletNav(
            BTCPayWalletProvider walletProvider,
            BTCPayNetworkProvider networkProvider,
            UIWalletsController walletsController,
            CurrencyNameTable currencies,
            IRateProvider rateProvider)
        {
            _walletProvider = walletProvider;
            _networkProvider = networkProvider;
            _walletsController = walletsController;
            _currencies = currencies;
            _rateProvider = rateProvider;
        }

        public async Task<IViewComponentResult> InvokeAsync(WalletId walletId)
        {
            var store = ViewContext.HttpContext.GetStoreData();
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var wallet = _walletProvider.GetWallet(network);
            var defaultCurrency = store.GetStoreBlob().DefaultCurrency;
            var derivation = store.GetDerivationSchemeSettings(_networkProvider, walletId.CryptoCode);
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
                var rates = await _rateProvider.GetRatesAsync(default);
                var rate = rates.FirstOrDefault(pair => pair.CurrencyPair.Right == defaultCurrency &&
                                                        pair.CurrencyPair.Left == network.CryptoCode);
                if (rate != null)
                {
                    var currencyData = _currencies.GetCurrencyData(defaultCurrency, false);
                    vm.BalanceDefaultCurrency = (balance.GetValue() * rate.BidAsk.Bid).ShowMoney(currencyData.Divisibility);
                }
            }

            return View(vm);
        }
    }
}
