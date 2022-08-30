#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
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
        private readonly BTCPayNetworkProvider _networkProvider;

        public WalletNav(
            BTCPayWalletProvider walletProvider,
            BTCPayNetworkProvider networkProvider,
            UIWalletsController walletsController)
        {
            _walletProvider = walletProvider;
            _networkProvider = networkProvider;
            _walletsController = walletsController;
        }

        public async Task<IViewComponentResult> InvokeAsync(WalletId walletId)
        {
            var store = ViewContext.HttpContext.GetStoreData();
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var wallet = _walletProvider.GetWallet(network);
            var derivation = store.GetDerivationSchemeSettings(_networkProvider, walletId.CryptoCode);
            var balance = await _walletsController.GetBalanceString(wallet, derivation?.AccountDerivation);
            
            var vm = new WalletNavViewModel
            {
                WalletId = walletId,
                Network = network,
                Balance = balance,
                Label = derivation?.Label ?? $"{store.StoreName} {walletId.CryptoCode} Wallet"
            };

            return View(vm);
        }
    }
}
