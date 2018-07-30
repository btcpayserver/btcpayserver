﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using LedgerWallet;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet]
        [Route("{storeId}/derivations/{cryptoCode}")]
        public IActionResult AddDerivationScheme(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            var network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            DerivationSchemeViewModel vm = new DerivationSchemeViewModel();
            vm.ServerUrl = WalletsController.GetLedgerWebsocketUrl(this.HttpContext, cryptoCode, null);
            vm.CryptoCode = cryptoCode;
            vm.RootKeyPath = network.GetRootKeyPath();
            SetExistingValues(store, vm);
            return View(vm);
        }

        private void SetExistingValues(StoreData store, DerivationSchemeViewModel vm)
        {
            var strategy = GetExistingDerivationStrategy(vm.CryptoCode, store);
            vm.DerivationScheme = strategy?.DerivationStrategyBase.ToString();
            vm.Enabled = strategy?.Enabled ?? false;
        }

        private DerivationStrategy GetExistingDerivationStrategy(string cryptoCode, StoreData store)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike);
            var existing = store.GetSupportedPaymentMethods(_NetworkProvider, false)
                .OfType<DerivationStrategy>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }

        [HttpPost]
        [Route("{storeId}/derivations/{cryptoCode}")]
        public async Task<IActionResult> AddDerivationScheme(string storeId, DerivationSchemeViewModel vm, string command, string cryptoCode)
        {
            vm.ServerUrl = WalletsController.GetLedgerWebsocketUrl(this.HttpContext, cryptoCode, null);
            vm.CryptoCode = cryptoCode;
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);
            if (network == null)
            {
                return NotFound();
            }
            vm.RootKeyPath = network.GetRootKeyPath();
            var wallet = _WalletProvider.GetWallet(network);
            if (wallet == null)
            {
                return NotFound();
            }

            PaymentMethodId paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
            DerivationStrategy strategy = null;
            try
            {
                if (!string.IsNullOrEmpty(vm.DerivationScheme))
                {
                    strategy = ParseDerivationStrategy(vm.DerivationScheme, null, network, vm.Enabled);
                    vm.DerivationScheme = strategy.ToString();
                    vm.Enabled = strategy.Enabled;
                }
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid Derivation Scheme");
                vm.Confirmation = false;
                return View(vm);
            }
            if (!vm.Confirmation && strategy != null)
                return ShowAddresses(vm, strategy);
            if (vm.Confirmation && !string.IsNullOrWhiteSpace(vm.HintAddress))
            {
                BitcoinAddress address = null;
                try
                {
                    address = BitcoinAddress.Create(vm.HintAddress, network.NBitcoinNetwork);
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.HintAddress), "Invalid hint address");
                    return ShowAddresses(vm, strategy);
                }

                try
                {
                    strategy = ParseDerivationStrategy(vm.DerivationScheme, address.ScriptPubKey, network, vm.Enabled);
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.HintAddress), "Impossible to find a match with this address");
                    return ShowAddresses(vm, strategy);
                }
                vm.HintAddress = "";
                vm.StatusMessage = "Address successfully found, please verify that the rest is correct and click on \"Confirm\"";
                ModelState.Remove(nameof(vm.HintAddress));
                ModelState.Remove(nameof(vm.DerivationScheme));
                return ShowAddresses(vm, strategy);
            }
            else
            {
                try
                {
                    if (strategy != null)                 
                        await wallet.TrackAsync(strategy.DerivationStrategyBase);
                    store.SetSupportedPaymentMethod(paymentMethodId, strategy);
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid Derivation Scheme");
                    return View(vm);
                }

               
                await _Repo.UpdateStore(store);
                StatusMessage = $"Derivation scheme for {network.CryptoCode} has been modified.";
                return RedirectToAction(nameof(UpdateStore), new { storeId = storeId });
            }
        }

        private IActionResult ShowAddresses(DerivationSchemeViewModel vm, DerivationStrategy strategy)
        {
            vm.DerivationScheme = strategy.DerivationStrategyBase.ToString();
            if (!string.IsNullOrEmpty(vm.DerivationScheme))
            {
                var line = strategy.DerivationStrategyBase.GetLineFor(DerivationFeature.Deposit);

                for (int i = 0; i < 10; i++)
                {
                    var address = line.Derive((uint)i);
                    vm.AddressSamples.Add((DerivationStrategyBase.GetKeyPath(DerivationFeature.Deposit).Derive((uint)i).ToString(), address.ScriptPubKey.GetDestinationAddress(strategy.Network.NBitcoinNetwork).ToString()));
                }
            }
            vm.Confirmation = true;
            return View(vm);
        }
    }
}
