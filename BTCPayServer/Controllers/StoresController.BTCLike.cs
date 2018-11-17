using System;
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
            vm.CryptoCode = cryptoCode;
            vm.RootKeyPath = network.GetRootKeyPath();
            SetExistingValues(store, vm);
            return View(vm);
        }

        [HttpGet]
        [Route("{storeId}/derivations/{cryptoCode}/ledger/ws")]
        public async Task<IActionResult> AddDerivationSchemeLedger(
            string storeId,
            string cryptoCode,
            string command,
            int account = 0)
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();

            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var hw = new HardwareWalletService(webSocket);
            object result = null;
            var network = _NetworkProvider.GetNetwork(cryptoCode);

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
                        var getxpubResult = await hw.GetExtPubKey(network, account, normalOperationTimeout.Token);
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
                        var bytes = UTF8NOBOM.GetBytes(JsonConvert.SerializeObject(result, MvcJsonOptions.Value.SerializerSettings));
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
            vm.DerivationScheme = GetExistingDerivationStrategy(vm.CryptoCode, store)?.DerivationStrategyBase.ToString();
            vm.Enabled = !store.GetStoreBlob().IsExcluded(new PaymentMethodId(vm.CryptoCode, PaymentTypes.BTCLike));
        }

        private DerivationStrategy GetExistingDerivationStrategy(string cryptoCode, StoreData store)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike);
            var existing = store.GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<DerivationStrategy>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }

        [HttpPost]
        [Route("{storeId}/derivations/{cryptoCode}")]
        public async Task<IActionResult> AddDerivationScheme(string storeId, DerivationSchemeViewModel vm, string cryptoCode)
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
            vm.RootKeyPath = network.GetRootKeyPath();
            var wallet = _WalletProvider.GetWallet(network);
            if (wallet == null)
            {
                return NotFound();
            }

            PaymentMethodId paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
            var exisingStrategy = store.GetSupportedPaymentMethods(_NetworkProvider)
                                       .Where(c => c.PaymentId == paymentMethodId)
                                       .OfType<DerivationStrategy>()
                                       .Select(c => c.DerivationStrategyBase.ToString())
                                       .FirstOrDefault();
            DerivationStrategy strategy = null;
            try
            {
                if (!string.IsNullOrEmpty(vm.DerivationScheme))
                {
                    strategy = ParseDerivationStrategy(vm.DerivationScheme, null, network);
                    vm.DerivationScheme = strategy.ToString();
                }
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid Derivation Scheme");
                vm.Confirmation = false;
                return View(vm);
            }
            var storeBlob = store.GetStoreBlob();
            var wasExcluded = storeBlob.GetExcludedPaymentMethods().Match(paymentMethodId);
            var willBeExcluded = !vm.Enabled;

            var showAddress = // Show addresses if:
                              // - If the user is testing the hint address in confirmation screen
                              (vm.Confirmation && !string.IsNullOrWhiteSpace(vm.HintAddress)) ||
                              // - The user is setting a new derivation scheme
                              (!vm.Confirmation && strategy != null && exisingStrategy != strategy.DerivationStrategyBase.ToString()) ||
                              // - The user is clicking on continue without changing anything   
                              (!vm.Confirmation && willBeExcluded == wasExcluded);

            showAddress = showAddress && strategy != null;
            if (!showAddress)
            {
                try
                {
                    if (strategy != null)
                        await wallet.TrackAsync(strategy.DerivationStrategyBase);
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
                StatusMessage = $"Derivation scheme for {network.CryptoCode} has been modified.";
                return RedirectToAction(nameof(UpdateStore), new { storeId = storeId });
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
                    strategy = ParseDerivationStrategy(vm.DerivationScheme, address.ScriptPubKey, network);
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
            }
            return ShowAddresses(vm, strategy);
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
