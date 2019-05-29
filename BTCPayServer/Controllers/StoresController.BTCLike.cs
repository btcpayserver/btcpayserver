﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using LedgerWallet;
using Microsoft.AspNetCore.Http;
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
            vm.CryptoCode = cryptoCode;
            vm.RootKeyPath = network.GetRootKeyPath();
            vm.Network = network;
            SetExistingValues(store, vm);
            return View(vm);
        }

        class GetXPubs
        {
            public BitcoinExtPubKey ExtPubKey { get; set; }
            public DerivationStrategyBase DerivationScheme { get; set; }
            public HDFingerprint RootFingerprint { get; set; }
            public string Source { get; set; }
        }

        [HttpGet]
        [Route("{storeId}/derivations/{cryptoCode}/ledger/ws")]
        public async Task<IActionResult> AddDerivationSchemeLedger(
            string storeId,
            string cryptoCode,
            string command,
            string keyPath = "")
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();

            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var hw = new LedgerHardwareWalletService(webSocket);
            object result = null;
            var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);

            using (var normalOperationTimeout = new CancellationTokenSource())
            {
                normalOperationTimeout.CancelAfter(TimeSpan.FromMinutes(30));
                try
                {
                    if (command == "test")
                    {
                        result = await hw.Test(normalOperationTimeout.Token);
                    }
                    if (command == "getxpub")
                    {
                        var k = KeyPath.Parse(keyPath);
                        if (k.Indexes.Length == 0)
                            throw new FormatException("Invalid key path");

                        var getxpubResult = new GetXPubs();
                        getxpubResult.ExtPubKey = await hw.GetExtPubKey(network, k, normalOperationTimeout.Token);
                        var segwit = network.NBitcoinNetwork.Consensus.SupportSegwit;
                        var derivation = new DerivationStrategyFactory(network.NBitcoinNetwork).CreateDirectDerivationStrategy(getxpubResult.ExtPubKey, new DerivationStrategyOptions()
                        {
                            P2SH = segwit,
                            Legacy = !segwit
                        });
                        getxpubResult.DerivationScheme = derivation;
                        getxpubResult.RootFingerprint = (await hw.GetExtPubKey(network, new KeyPath(), normalOperationTimeout.Token)).ExtPubKey.PubKey.GetHDFingerPrint();
                        getxpubResult.Source = hw.Device;
                        result = getxpubResult;
                    }
                }
                catch (OperationCanceledException)
                { result = new LedgerTestResult() { Success = false, Error = "Timeout" }; }
                catch (Exception ex)
                { result = new LedgerTestResult() { Success = false, Error = ex.Message }; }
                finally { hw.Dispose(); }
                try
                {
                    if (result != null)
                    {
                        UTF8Encoding UTF8NOBOM = new UTF8Encoding(false);
                        var bytes = UTF8NOBOM.GetBytes(JsonConvert.SerializeObject(result, network.NBXplorerNetwork.JsonSerializerSettings));
                        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, new CancellationTokenSource(2000).Token);
                    }
                }
                catch { }
                finally
                {
                    await webSocket.CloseSocket();
                }
            }
            return new EmptyResult();
        }

        
        
        private void SetExistingValues(StoreData store, DerivationSchemeViewModel vm)
        {
            var derivation = GetExistingDerivationStrategy(vm.CryptoCode, store);
            if (derivation != null)
            {
                vm.DerivationScheme = derivation.AccountDerivation.ToString();
                vm.Config = derivation.ToJson();
            }
            vm.Enabled = !store.GetStoreBlob().IsExcluded(new PaymentMethodId(vm.CryptoCode, PaymentTypes.BTCLike));
        }

        private DerivationSchemeSettings GetExistingDerivationStrategy(string cryptoCode, StoreData store)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike);
            var existing = store.GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<DerivationSchemeSettings>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }
        
        

        [HttpPost]
        [Route("{storeId}/derivations/{cryptoCode}")]
        public async Task<IActionResult> AddDerivationScheme(string storeId, DerivationSchemeViewModel vm,
            string cryptoCode)
        {
            vm.CryptoCode = cryptoCode;
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            vm.Network = network;
            vm.RootKeyPath = network.GetRootKeyPath();
            DerivationSchemeSettings strategy = null;
            
            var wallet = _WalletProvider.GetWallet(network);
            if (wallet == null)
            {
                return NotFound();
            }

            if (!string.IsNullOrEmpty(vm.Config))
            {
                if (!DerivationSchemeSettings.TryParseFromJson(vm.Config, network, out strategy))
                {
                    vm.StatusMessage = new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = "Config file was not in the correct format"
                    }.ToString();
                    vm.Confirmation = false;
                    return View(vm);
                }
            }

            if (vm.ColdcardPublicFile != null)
            {
                if (!DerivationSchemeSettings.TryParseFromColdcard(await ReadAllText(vm.ColdcardPublicFile), network, out strategy))
                {
                    vm.StatusMessage = new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = "Coldcard public file was not in the correct format"
                    }.ToString();
                    vm.Confirmation = false;
                    return View(vm);
                }
            }
            else
            {
                try
                {
                    if (!string.IsNullOrEmpty(vm.DerivationScheme))
                    {
                        var newStrategy = ParseDerivationStrategy(vm.DerivationScheme, null, network);
                        if (newStrategy.AccountDerivation != strategy?.AccountDerivation)
                        {
                            var accountKey = string.IsNullOrEmpty(vm.AccountKey) ? null : new BitcoinExtPubKey(vm.AccountKey, network.NBitcoinNetwork);
                            if (accountKey != null)
                            {
                                var accountSettings = newStrategy.AccountKeySettings.FirstOrDefault(a => a.AccountKey == accountKey);
                                if (accountSettings != null)
                                {
                                    accountSettings.AccountKeyPath = vm.KeyPath == null ? null : KeyPath.Parse(vm.KeyPath);
                                    accountSettings.RootFingerprint = string.IsNullOrEmpty(vm.RootFingerprint) ? (HDFingerprint?)null : new HDFingerprint(NBitcoin.DataEncoders.Encoders.Hex.DecodeData(vm.RootFingerprint));
                                }
                            }
                            strategy = newStrategy;
                            strategy.Source = vm.Source;
                            vm.DerivationScheme = strategy.AccountDerivation.ToString();
                        }
                    }
                    else
                    {
                        strategy = null;
                    }
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid Derivation Scheme");
                    vm.Confirmation = false;
                    return View(vm);
                }
            }

            var oldConfig = vm.Config;
            vm.Config = strategy == null ? null : strategy.ToJson();

            PaymentMethodId paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
            var exisingStrategy = store.GetSupportedPaymentMethods(_NetworkProvider)
                .Where(c => c.PaymentId == paymentMethodId)
                .OfType<DerivationSchemeSettings>()
                .FirstOrDefault();
            var storeBlob = store.GetStoreBlob();
            var wasExcluded = storeBlob.GetExcludedPaymentMethods().Match(paymentMethodId);
            var willBeExcluded = !vm.Enabled;

            var showAddress = // Show addresses if:
                // - If the user is testing the hint address in confirmation screen
                (vm.Confirmation && !string.IsNullOrWhiteSpace(vm.HintAddress)) ||
                // - The user is clicking on continue after changing the config
                (!vm.Confirmation && oldConfig != vm.Config) ||
                // - The user is clickingon continue without changing config nor enabling/disabling
                (!vm.Confirmation && oldConfig == vm.Config && willBeExcluded == wasExcluded);

            showAddress = showAddress && strategy != null;
            if (!showAddress)
            {
                try
                {
                    if (strategy != null)
                        await wallet.TrackAsync(strategy.AccountDerivation);
                    store.SetSupportedPaymentMethod(paymentMethodId, strategy);
                    storeBlob.SetExcluded(paymentMethodId, willBeExcluded);
                    store.SetStoreBlob(storeBlob);
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid Derivation Scheme");
                    return View(vm);
                }

                await _Repo.UpdateStore(store);
                if (oldConfig != vm.Config)
                    StatusMessage = $"Derivation settings for {network.CryptoCode} has been modified.";
                if (willBeExcluded != wasExcluded)
                {
                    var label = willBeExcluded ? "disabled" : "enabled";
                    StatusMessage = $"On-Chain payments for {network.CryptoCode} has been {label}.";
                }
                return RedirectToAction(nameof(UpdateStore), new {storeId = storeId});
            }
            else if (!string.IsNullOrEmpty(vm.HintAddress))
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
                    var newStrategy = ParseDerivationStrategy(vm.DerivationScheme, address.ScriptPubKey, network);
                    if (newStrategy.AccountDerivation != strategy.AccountDerivation)
                    {
                        strategy.AccountDerivation = newStrategy.AccountDerivation;
                        strategy.AccountOriginal = null;
                    }
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.HintAddress), "Impossible to find a match with this address");
                    return ShowAddresses(vm, strategy);
                }

                vm.HintAddress = "";
                vm.StatusMessage =
                    "Address successfully found, please verify that the rest is correct and click on \"Confirm\"";
                ModelState.Remove(nameof(vm.HintAddress));
                ModelState.Remove(nameof(vm.DerivationScheme));
            }

            return ShowAddresses(vm, strategy);
        }

        private async Task<string> ReadAllText(IFormFile file)
        {
            using (var stream = new StreamReader(file.OpenReadStream()))
            {
                return await stream.ReadToEndAsync();
            }
        }

        private IActionResult ShowAddresses(DerivationSchemeViewModel vm, DerivationSchemeSettings strategy)
        {
            vm.DerivationScheme = strategy.AccountDerivation.ToString();
            if (!string.IsNullOrEmpty(vm.DerivationScheme))
            {
                var line = strategy.AccountDerivation.GetLineFor(DerivationFeature.Deposit);

                for (int i = 0; i < 10; i++)
                {
                    var address = line.Derive((uint)i);
                    vm.AddressSamples.Add((DerivationStrategyBase.GetKeyPath(DerivationFeature.Deposit).Derive((uint)i).ToString(), address.ScriptPubKey.GetDestinationAddress(strategy.Network.NBitcoinNetwork).ToString()));
                }
            }
            vm.Confirmation = true;
            ModelState.Remove(nameof(vm.Config)); // Remove the cached value
            return View(vm);
        }
    }
}
