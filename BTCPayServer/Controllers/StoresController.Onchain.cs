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
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet("{storeId}/onchain/{cryptoCode}")]
        public ActionResult SetupWallet(WalletSetupViewModel vm)
        {
            var checkResult = IsAvailable(vm.CryptoCode, out var store, out _);
            if (checkResult != null)
            {
                return checkResult;
            }

            var derivation = GetExistingDerivationStrategy(vm.CryptoCode, store);
            vm.DerivationScheme = derivation?.AccountDerivation.ToString();

            return View(vm);
        }

        [HttpGet("{storeId}/onchain/{cryptoCode}/import/{method?}")]
        public async Task<IActionResult> ImportWallet(WalletSetupViewModel vm)
        {
            var checkResult = IsAvailable(vm.CryptoCode, out _, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            var (hotWallet, rpcImport) = await CanUseHotWallet();
            vm.Network = network;
            vm.RootKeyPath = network.GetRootKeyPath();
            vm.CanUseHotWallet = hotWallet;
            vm.CanUseRPCImport = rpcImport;

            if (vm.Method == null)
            {
                vm.Method = WalletSetupMethod.ImportOptions;
            }
            else if (vm.Method == WalletSetupMethod.Seed)
            {
                vm.SetupRequest = new GenerateWalletRequest();
            }

            return View(vm.ViewName, vm);
        }

        [HttpPost("{storeId}/onchain/{cryptoCode}/modify")]
        [HttpPost("{storeId}/onchain/{cryptoCode}/import/{method}")]
        public async Task<IActionResult> UpdateWallet(WalletSetupViewModel vm)
        {
            var checkResult = IsAvailable(vm.CryptoCode, out var store, out var network);
            if (checkResult != null)
            {
                return checkResult;
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
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid wallet format");
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
                    ModelState.AddModelError(nameof(vm.HintAddress), "Impossible to find a match with this address. Are you sure the wallet and address provided are correct and from the same source?");
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

        [HttpGet("{storeId}/onchain/{cryptoCode}/generate/{method?}")]
        public async Task<IActionResult> GenerateWallet(WalletSetupViewModel vm)
        {
            var checkResult = IsAvailable(vm.CryptoCode, out var store, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            var isHotWallet = vm.Method == WalletSetupMethod.HotWallet;
            var (hotWallet, rpcImport) = await CanUseHotWallet();
            if (isHotWallet && !hotWallet)
            {
                return NotFound();
            }

            var derivation = GetExistingDerivationStrategy(vm.CryptoCode, store);
            if (derivation != null)
            {
                vm.DerivationScheme = derivation.AccountDerivation.ToString();
                vm.Config = derivation.ToJson();
            }

            vm.Enabled = !store.GetStoreBlob().IsExcluded(new PaymentMethodId(vm.CryptoCode, PaymentTypes.BTCLike));
            vm.CanUseHotWallet = hotWallet;
            vm.CanUseRPCImport = rpcImport;
            vm.RootKeyPath = network.GetRootKeyPath();
            vm.Network = network;

            if (vm.Method == null)
            {
                vm.Method = WalletSetupMethod.GenerateOptions;
            }
            else
            {
                vm.SetupRequest = new GenerateWalletRequest { SavePrivateKeys = isHotWallet };
            }

            return View(vm.ViewName, vm);
        }

        [HttpPost("{storeId}/onchain/{cryptoCode}/generate/{method}")]
        public async Task<IActionResult> GenerateWallet(string storeId, string cryptoCode, WalletSetupMethod method, GenerateWalletRequest request)
        {
            var checkResult = IsAvailable(cryptoCode, out var store, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            var (hotWallet, rpcImport) = await CanUseHotWallet();
            if (!hotWallet && request.SavePrivateKeys || !rpcImport && request.ImportKeysToRPC)
            {
                return NotFound();
            }

            var client = _ExplorerProvider.GetExplorerClient(cryptoCode);
            var isImport = method == WalletSetupMethod.Seed;
            var vm = new WalletSetupViewModel
            {
                StoreId = storeId,
                CryptoCode = cryptoCode,
                Method = method,
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

            if (isImport && string.IsNullOrEmpty(request.ExistingMnemonic))
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

            if (!isImport)
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
        [HttpGet("{storeId}/onchain/{cryptoCode}/generate/confirm")]
        public ActionResult GenerateWalletConfirm(string storeId, string cryptoCode)
        {
            var checkResult = IsAvailable(cryptoCode, out _, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            TempData[WellKnownTempData.SuccessMessage] =
                $"Derivation settings for {network.CryptoCode} have been modified.";

            return RedirectToAction(nameof(UpdateStore), new {storeId});
        }

        [HttpGet("{storeId}/onchain/{cryptoCode}/modify")]
        public async Task<IActionResult> ModifyWallet(WalletSetupViewModel vm)
        {
            var checkResult = IsAvailable(vm.CryptoCode, out var store, out var network);
            if (checkResult != null)
            {
                return checkResult;
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

        [HttpGet("{storeId}/onchain/{cryptoCode}/delete")]
        public IActionResult DeleteWallet(string storeId, string cryptoCode)
        {
            var checkResult = IsAvailable(cryptoCode, out var store, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            var derivation = GetExistingDerivationStrategy(cryptoCode, store);
            var description =
                (derivation.IsHotWallet ? "<p class=\"text-danger font-weight-bold\">Please note that this is a hot wallet!</p> " : "") +
                "<p class=\"text-danger font-weight-bold\">Do not remove the wallet if you have not backed it up!</p>" +
                "<p class=\"text-left mb-0\">Removing the wallet will erase the wallet data from the server. " +
                $"The store won't be able to receive {network.CryptoCode} onchain payments until a new wallet is set up.</p>";

            return View("Confirm", new ConfirmModel
            {
                Title = $"Remove {network.CryptoCode} wallet",
                Description = description,
                DescriptionHtml = true,
                Action = "Remove"
            });
        }

        [HttpPost("{storeId}/onchain/{cryptoCode}/delete")]
        public async Task<IActionResult> ConfirmDeleteWallet(string storeId, string cryptoCode)
        {
            var checkResult = IsAvailable(cryptoCode, out var store, out var network);
            if (checkResult != null)
            {
                return checkResult;
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

        private ActionResult IsAvailable(string cryptoCode, out StoreData store, out BTCPayNetwork network)
        {
            store = HttpContext.GetStoreData();
            network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);

            return store == null || network == null ? NotFound() : null;
        }
    }
}
