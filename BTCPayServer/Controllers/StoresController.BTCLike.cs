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
        public async Task<IActionResult> AddDerivationScheme(string storeId, string cryptoCode)
        {
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();
            var network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            DerivationSchemeViewModel vm = new DerivationSchemeViewModel();
            vm.ServerUrl = GetStoreUrl(storeId);
            vm.CryptoCode = cryptoCode;
            vm.RootKeyPath = network.NBitcoinNetwork.Consensus.SupportSegwit ? "49'" : "44'";
            SetExistingValues(store, vm);
            return View(vm);
        }

        private void SetExistingValues(StoreData store, DerivationSchemeViewModel vm)
        {
            vm.DerivationScheme = GetExistingDerivationStrategy(vm.CryptoCode, store)?.DerivationStrategyBase.ToString();
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
            vm.ServerUrl = GetStoreUrl(storeId);
            vm.CryptoCode = cryptoCode;
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();

            var network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);
            if (network == null)
            {
                return NotFound();
            }
            vm.RootKeyPath = network.NBitcoinNetwork.Consensus.SupportSegwit ? "49'" : "44'";
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


        public class GetInfoResult
        {
            public int RecommendedSatoshiPerByte { get; set; }
            public double Balance { get; set; }
        }

        public class SendToAddressResult
        {
            public string TransactionId { get; set; }
        }

        [HttpGet]
        [Route("{storeId}/ws/ledger")]
        public async Task<IActionResult> LedgerConnection(
            string storeId,
            string command,
            // getinfo
            string cryptoCode = null,
            // getxpub
            int account = 0,
            // sendtoaddress
            string destination = null, string amount = null, string feeRate = null, string substractFees = null
            )
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();

            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            var hw = new HardwareWalletService(webSocket);
            object result = null;
            try
            {
                BTCPayNetwork network = null;
                if (cryptoCode != null)
                {
                    network = _NetworkProvider.GetNetwork(cryptoCode);
                    if (network == null)
                        throw new FormatException("Invalid value for crypto code");
                }

                BitcoinAddress destinationAddress = null;
                if (destination != null)
                {
                    try
                    {
                        destinationAddress = BitcoinAddress.Create(destination, network.NBitcoinNetwork);
                    }
                    catch { }
                    if (destinationAddress == null)
                        throw new FormatException("Invalid value for destination");
                }

                FeeRate feeRateValue = null;
                if (feeRate != null)
                {
                    try
                    {
                        feeRateValue = new FeeRate(Money.Satoshis(int.Parse(feeRate, CultureInfo.InvariantCulture)), 1);
                    }
                    catch { }
                    if (feeRateValue == null || feeRateValue.FeePerK <= Money.Zero)
                        throw new FormatException("Invalid value for fee rate");
                }

                Money amountBTC = null;
                if (amount != null)
                {
                    try
                    {
                        amountBTC = Money.Parse(amount);
                    }
                    catch { }
                    if (amountBTC == null || amountBTC <= Money.Zero)
                        throw new FormatException("Invalid value for amount");
                }

                bool subsctractFeesValue = false;
                if (substractFees != null)
                {
                    try
                    {
                        subsctractFeesValue = bool.Parse(substractFees);
                    }
                    catch { throw new FormatException("Invalid value for subtract fees"); }
                }
                if (command == "test")
                {
                    result = await hw.Test();
                }
                if (command == "getxpub")
                {
                    var getxpubResult = await hw.GetExtPubKey(network, account);
                    ;
                    getxpubResult.CoinType = (int)(getxpubResult.KeyPath.Indexes[1] - 0x80000000);
                    result = getxpubResult;
                }
                if (command == "getinfo")
                {
                    var strategy = GetDirectDerivationStrategy(store, network);
                    var strategyBase = GetDerivationStrategy(store, network);
                    if (strategy == null || !await hw.SupportDerivation(network, strategy))
                    {
                        throw new Exception($"This store is not configured to use this ledger");
                    }

                    var feeProvider = _FeeRateProvider.CreateFeeProvider(network);
                    var recommendedFees = feeProvider.GetFeeRateAsync();
                    var balance = _WalletProvider.GetWallet(network).GetBalance(strategyBase);
                    result = new GetInfoResult() { Balance = (double)(await balance).ToDecimal(MoneyUnit.BTC), RecommendedSatoshiPerByte = (int)(await recommendedFees).GetFee(1).Satoshi };
                }

                if (command == "sendtoaddress")
                {
                    if (!_Dashboard.IsFullySynched(network.CryptoCode, out var summary))
                        throw new Exception($"{network.CryptoCode}: not started or fully synched");
                    var strategy = GetDirectDerivationStrategy(store, network);
                    var strategyBase = GetDerivationStrategy(store, network);
                    var wallet = _WalletProvider.GetWallet(network);
                    var change = wallet.GetChangeAddressAsync(strategyBase);

                    var unspentCoins = await wallet.GetUnspentCoins(strategyBase);
                    var changeAddress = await change;
                    var transaction = await hw.SendToAddress(strategy, unspentCoins, network,
                                            new[] { (destinationAddress as IDestination, amountBTC, subsctractFeesValue) },
                                            feeRateValue,
                                            changeAddress.Item1,
                                            changeAddress.Item2, summary.Status.BitcoinStatus.MinRelayTxFee);
                    try
                    {
                        var broadcastResult = await wallet.BroadcastTransactionsAsync(new List<Transaction>() { transaction });
                        if (!broadcastResult[0].Success)
                        {
                            throw new Exception($"RPC Error while broadcasting: {broadcastResult[0].RPCCode} {broadcastResult[0].RPCCodeMessage} {broadcastResult[0].RPCMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Error while broadcasting: " + ex.Message);
                    }
                    wallet.InvalidateCache(strategyBase);
                    result = new SendToAddressResult() { TransactionId = transaction.GetHash().ToString() };
                }
            }
            catch (OperationCanceledException)
            { result = new LedgerTestResult() { Success = false, Error = "Timeout" }; }
            catch (Exception ex)
            { result = new LedgerTestResult() { Success = false, Error = ex.Message }; }

            try
            {
                if (result != null)
                {
                    UTF8Encoding UTF8NOBOM = new UTF8Encoding(false);
                    var bytes = UTF8NOBOM.GetBytes(JsonConvert.SerializeObject(result, _MvcJsonOptions.SerializerSettings));
                    await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, new CancellationTokenSource(2000).Token);
                }
            }
            catch { }
            finally
            {
                await webSocket.CloseSocket();
            }

            return new EmptyResult();
        }

        private DirectDerivationStrategy GetDirectDerivationStrategy(StoreData store, BTCPayNetwork network)
        {
            var strategy = GetDerivationStrategy(store, network);
            var directStrategy = strategy as DirectDerivationStrategy;
            if (directStrategy == null)
                directStrategy = (strategy as P2SHDerivationStrategy).Inner as DirectDerivationStrategy;
            if (!directStrategy.Segwit)
                return null;
            return directStrategy;
        }

        private DerivationStrategyBase GetDerivationStrategy(StoreData store, BTCPayNetwork network)
        {
            var strategy = store
                            .GetSupportedPaymentMethods(_NetworkProvider)
                            .OfType<DerivationStrategy>()
                            .FirstOrDefault(s => s.Network.NBitcoinNetwork == network.NBitcoinNetwork);
            if (strategy == null)
            {
                throw new Exception($"Derivation strategy for {network.CryptoCode} is not set");
            }

            return strategy.DerivationStrategyBase;
        }
    }
}
