using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet]
        [Route("{storeId}/wallet/{cryptoCode}")]
        public async Task<IActionResult> SetupWallet(WalletSetupViewModel vm)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = vm.CryptoCode == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            var derivation = GetExistingDerivationStrategy(vm.CryptoCode, store);
            var (hotWallet, rpcImport) = await CanUseHotWallet();
            vm.CanUseHotWallet = hotWallet;
            vm.CanUseRPCImport = rpcImport;
            vm.DerivationScheme = derivation?.AccountDerivation.ToString();

            return View(vm);
        }

        [HttpGet]
        [Route("{storeId}/wallet/{cryptoCode}/import/{method?}")]
        public async Task<IActionResult> ImportWallet(WalletSetupViewModel vm)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = vm.CryptoCode == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            var (hotWallet, rpcImport) = await CanUseHotWallet();
            vm.Network = network;
            vm.RootKeyPath = network.GetRootKeyPath();
            vm.CanUseHotWallet = hotWallet;
            vm.CanUseRPCImport = rpcImport;

            if (vm.Method == null)
            {
                vm.Method = WalletSetupMethod.Import;
            }
            else if (vm.Method == WalletSetupMethod.Seed)
            {
                vm.SetupRequest = new WalletSetupRequest {RequireExistingMnemonic = true};
            }

            return View(vm.ViewName, vm);
        }

        [HttpPost]
        [Route("{storeId}/wallet/{cryptoCode}/modify")]
        [Route("{storeId}/wallet/{cryptoCode}/import/{method}")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> UpdateWallet(WalletSetupViewModel vm)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = vm.CryptoCode == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCode);
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
                    ModelState.AddModelError(nameof(vm.Config), "Config file was not in the correct format");
                    return View(vm.ViewName, vm);
                }
            }

            if (vm.WalletFile != null)
            {
                if (!DerivationSchemeSettings.TryParseFromWalletFile(await ReadAllText(vm.WalletFile), network, out strategy))
                {
                    ModelState.AddModelError(nameof(vm.WalletFile), "Wallet file was not in the correct format");
                    return View(vm.ViewName, vm);
                }
            }
            else if (!string.IsNullOrEmpty(vm.WalletFileContent))
            {
                if (!DerivationSchemeSettings.TryParseFromWalletFile(vm.WalletFileContent, network, out strategy))
                {
                    ModelState.AddModelError(nameof(vm.WalletFileContent), "QR import was not in the correct format");
                    return View(vm.ViewName, vm);
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
                            var accountKey = string.IsNullOrEmpty(vm.AccountKey)
                                ? null
                                : new BitcoinExtPubKey(vm.AccountKey, network.NBitcoinNetwork);
                            if (accountKey != null)
                            {
                                var accountSettings =
                                    newStrategy.AccountKeySettings.FirstOrDefault(a => a.AccountKey == accountKey);
                                if (accountSettings != null)
                                {
                                    accountSettings.AccountKeyPath =
                                        vm.KeyPath == null ? null : KeyPath.Parse(vm.KeyPath);
                                    accountSettings.RootFingerprint = string.IsNullOrEmpty(vm.RootFingerprint)
                                        ? (HDFingerprint?)null
                                        : new HDFingerprint(
                                            NBitcoin.DataEncoders.Encoders.Hex.DecodeData(vm.RootFingerprint));
                                }
                            }

                            strategy = newStrategy;
                            strategy.Source = vm.Source;
                            vm.DerivationScheme = strategy.AccountDerivation.ToString();
                        }
                    }
                    else
                    {
                        ModelState.AddModelError(nameof(vm.DerivationScheme), "Please provide your extended public key");
                        return View(vm.ViewName, vm);
                    }
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid extended public key");
                    return View(vm.ViewName, vm);
                }
            }

            var oldConfig = vm.Config;
            vm.Config = strategy?.ToJson();
            var configChanged = oldConfig != vm.Config;
            PaymentMethodId paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
            var storeBlob = store.GetStoreBlob();
            var wasExcluded = storeBlob.GetExcludedPaymentMethods().Match(paymentMethodId);
            var willBeExcluded = !vm.Enabled;
            var excludedChanged = willBeExcluded != wasExcluded;

            var showAddress = // Show addresses if:
                // - If the user is testing the hint address in confirmation screen
                (vm.Confirmation && !string.IsNullOrWhiteSpace(vm.HintAddress)) ||
                // - The user is clicking on continue after changing the config
                (!vm.Confirmation && configChanged);

            showAddress = showAddress && strategy != null;
            if (!showAddress)
            {
                try
                {
                    if (strategy != null)
                        await wallet.TrackAsync(strategy.AccountDerivation);
                    store.SetSupportedPaymentMethod(paymentMethodId, strategy);
                    storeBlob.SetExcluded(paymentMethodId, willBeExcluded);
                    storeBlob.Hints.Wallet = false;
                    store.SetStoreBlob(storeBlob);
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid derivation scheme");
                    return View(vm.ViewName, vm);
                }

                await _Repo.UpdateStore(store);
                _EventAggregator.Publish(new WalletChangedEvent {WalletId = new WalletId(vm.StoreId, vm.CryptoCode)});

                if (excludedChanged)
                {
                    var label = willBeExcluded ? "disabled" : "enabled";
                    TempData[WellKnownTempData.SuccessMessage] =
                        $"On-Chain payments for {network.CryptoCode} have been {label}.";
                }
                else
                {
                    TempData[WellKnownTempData.SuccessMessage] =
                        $"Derivation settings for {network.CryptoCode} have been modified.";
                }

                // This is success case when derivation scheme is added to the store
                return RedirectToAction(nameof(UpdateStore), new {storeId = vm.StoreId});
            }

            if (!string.IsNullOrEmpty(vm.HintAddress))
            {
                BitcoinAddress address;
                try
                {
                    address = BitcoinAddress.Create(vm.HintAddress, network.NBitcoinNetwork);
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.HintAddress), "Invalid hint address");
                    return ConfirmAddresses(vm, strategy);
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
                    return ConfirmAddresses(vm, strategy);
                }

                vm.HintAddress = "";
                TempData[WellKnownTempData.SuccessMessage] =
                    "Address successfully found, please verify that the rest is correct and click on \"Confirm\"";
                ModelState.Remove(nameof(vm.HintAddress));
                ModelState.Remove(nameof(vm.DerivationScheme));
            }

            return ConfirmAddresses(vm, strategy);
        }

        [HttpGet]
        [Route("{storeId}/wallet/{cryptoCode}/generate")]
        public async Task<IActionResult> GenerateWallet(WalletSetupViewModel vm)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = vm.CryptoCode == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            var derivation = GetExistingDerivationStrategy(vm.CryptoCode, store);
            if (derivation != null)
            {
                vm.DerivationScheme = derivation.AccountDerivation.ToString();
                vm.Config = derivation.ToJson();
            }

            var (hotWallet, rpcImport) = await CanUseHotWallet();

            vm.Enabled = !store.GetStoreBlob().IsExcluded(new PaymentMethodId(vm.CryptoCode, PaymentTypes.BTCLike));
            vm.CanUseHotWallet = hotWallet;
            vm.CanUseRPCImport = rpcImport;
            vm.RootKeyPath = network.GetRootKeyPath();
            vm.Network = network;
            vm.SetupRequest = new WalletSetupRequest();
            vm.Method = WalletSetupMethod.Generate;

            return View(vm);
        }

        [HttpPost]
        [Route("{storeId}/wallet/{cryptoCode}/generate")]
        public async Task<IActionResult> GenerateWallet(string storeId, string cryptoCode, WalletSetupRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var (hotWallet, rpcImport) = await CanUseHotWallet();
            if (!hotWallet || !rpcImport && request.ImportKeysToRPC)
            {
                return NotFound();
            }

            var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            var client = _ExplorerProvider.GetExplorerClient(cryptoCode);
            var vm = new WalletSetupViewModel
            {
                StoreId = storeId,
                CryptoCode = cryptoCode,
                Method = request.RequireExistingMnemonic ? WalletSetupMethod.Seed : WalletSetupMethod.Generate,
                SetupRequest = request,
                Confirmation = string.IsNullOrEmpty(request.ExistingMnemonic),
                Network = network,
                RootKeyPath = network.GetRootKeyPath(),
                Enabled = !store.GetStoreBlob().IsExcluded(new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike)),
                Source = "NBXplorer",
                DerivationSchemeFormat = "BTCPay",
                CanUseHotWallet = true,
                CanUseRPCImport = rpcImport
            };

            if (request.RequireExistingMnemonic && string.IsNullOrEmpty(request.ExistingMnemonic))
            {
                ModelState.AddModelError(nameof(request.ExistingMnemonic), "Please provide your existing seed");
                return View(vm.ViewName, vm);
            }

            GenerateWalletResponse response;
            try
            {
                response = await client.GenerateWalletAsync(request);
                if (response == null)
                {
                    throw new Exception("Node unavailable");
                }
            }
            catch (Exception e)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = $"There was an error generating your wallet: {e.Message}"
                });
                return View(vm.ViewName, vm);
            }

            // Set wallet properties from generate response
            vm.RootFingerprint = response.AccountKeyPath.MasterFingerprint.ToString();
            vm.DerivationScheme = response.DerivationScheme.ToString();
            vm.AccountKey = response.AccountHDKey.Neuter().ToWif();
            vm.KeyPath = response.AccountKeyPath.KeyPath.ToString();

            var result = await UpdateWallet(vm);

            if (!ModelState.IsValid || !(result is RedirectToActionResult))
                return result;

            TempData.Clear();

            if (!request.RequireExistingMnemonic)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Html = "<span class='text-centered'>Your wallet has been generated.</span>"
                });
                var seedVm = new RecoverySeedBackupViewModel
                {
                    CryptoCode = cryptoCode,
                    Mnemonic = response.Mnemonic,
                    Passphrase = response.Passphrase,
                    IsStored = request.SavePrivateKeys,
                    ReturnUrl = Url.Action(nameof(GenerateWalletConfirm), new {storeId, cryptoCode})
                };
                return this.RedirectToRecoverySeedBackup(seedVm);
            }

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Html = "Please check your addresses and confirm."
            });
            return result;
        }

        // The purpose of this action is to show the user a success message, which confirms
        // that the store settings have been updated after generating a new wallet.
        [HttpGet]
        [Route("{storeId}/wallet/{cryptoCode}/generate/confirm")]
        public ActionResult GenerateWalletConfirm(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            var wallet = _WalletProvider.GetWallet(network);
            if (wallet == null)
            {
                return NotFound();
            }

            TempData[WellKnownTempData.SuccessMessage] =
                $"Derivation settings for {network.CryptoCode} have been modified.";

            return RedirectToAction(nameof(UpdateStore), new {storeId});
        }

        [HttpGet]
        [Route("{storeId}/wallet/{cryptoCode}/modify")]
        public async Task<IActionResult> ModifyWallet(WalletSetupViewModel vm)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = vm.CryptoCode == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            var wallet = _WalletProvider.GetWallet(network);
            if (wallet == null)
            {
                return NotFound();
            }

            var derivation = GetExistingDerivationStrategy(vm.CryptoCode, store);
            if (derivation == null)
            {
                return NotFound();
            }

            var (hotWallet, rpcImport) = await CanUseHotWallet();
            vm.CanUseHotWallet = hotWallet;
            vm.CanUseRPCImport = rpcImport;
            vm.RootKeyPath = network.GetRootKeyPath();
            vm.Network = network;
            vm.Source = derivation.Source;
            vm.RootFingerprint = derivation.GetSigningAccountKeySettings().RootFingerprint.ToString();
            vm.DerivationScheme = derivation.AccountDerivation.ToString();
            vm.KeyPath = derivation.GetSigningAccountKeySettings().AccountKeyPath?.ToString();
            vm.Config = derivation.ToJson();
            vm.Enabled = !store.GetStoreBlob().IsExcluded(new PaymentMethodId(vm.CryptoCode, PaymentTypes.BTCLike));

            return View(vm);
        }

        [HttpGet]
        [Route("{storeId}/wallet/{cryptoCode}/delete")]
        public IActionResult DeleteWallet(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            return View("Confirm", new ConfirmModel
            {
                Title = $"Remove {network.CryptoCode} wallet",
                Description = $"This will erase the wallet data from the server. Do not remove the wallet if you have not backed it up. The store won't be able to receive {network.CryptoCode} onchain payments until a new wallet is set up.",
                Action = "Remove"
            });
        }

        [HttpPost]
        [Route("{storeId}/wallet/{cryptoCode}/delete")]
        public async Task<IActionResult> ConfirmDeleteWallet(string storeId, string cryptoCode)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            var wallet = _WalletProvider.GetWallet(network);
            if (wallet == null)
            {
                return NotFound();
            }

            var derivation = GetExistingDerivationStrategy(cryptoCode, store);
            if (derivation == null)
            {
                return NotFound();
            }

            PaymentMethodId paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
            store.SetSupportedPaymentMethod(paymentMethodId, null);

            await _Repo.UpdateStore(store);
            _EventAggregator.Publish(new WalletChangedEvent {WalletId = new WalletId(storeId, cryptoCode)});

            TempData[WellKnownTempData.SuccessMessage] =
                $"On-Chain payment for {network.CryptoCode} has been removed.";

            return RedirectToAction(nameof(UpdateStore), new {storeId});
        }

        private IActionResult ConfirmAddresses(WalletSetupViewModel vm, DerivationSchemeSettings strategy)
        {
            vm.DerivationScheme = strategy.AccountDerivation.ToString();
            var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);

            if (!string.IsNullOrEmpty(vm.DerivationScheme))
            {
                var line = strategy.AccountDerivation.GetLineFor(deposit);

                for (uint i = 0; i < 10; i++)
                {
                    var keyPath = deposit.GetKeyPath(i);
                    var rootedKeyPath = vm.GetAccountKeypath()?.Derive(keyPath);
                    var derivation = line.Derive(i);
                    var address = strategy.Network.NBXplorerNetwork.CreateAddress(strategy.AccountDerivation,
                        line.KeyPathTemplate.GetKeyPath(i),
                        derivation.ScriptPubKey).ToString();
                    vm.AddressSamples.Add((keyPath.ToString(), address, rootedKeyPath));
                }
            }

            vm.Confirmation = true;
            ModelState.Remove(nameof(vm.Config)); // Remove the cached value

            return View("ImportWallet/ConfirmAddresses", vm);
        }
    }
}
