using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.Models;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet]
        [Route("{storeId}/wallet/{cryptoCode}")]
        public ActionResult SetupWallet(SetupWalletViewModel vm)
        {
            return View(vm);
        }

        [HttpGet]
        [Route("{storeId}/wallet/{cryptoCode}/import/{method?}")]
        public ActionResult ImportWallet(ImportWalletViewModel vm)
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

            var view = vm.Method switch
            {
                WalletImportMethod.Hardware => "ImportWallet/Hardware",
                WalletImportMethod.Enter => "ImportWallet/Enter",
                WalletImportMethod.File => "ImportWallet/File",
                WalletImportMethod.Scan => "ImportWallet/Scan",
                _ => "ImportWallet"
            };

            return View(view, vm);
        }

        [HttpPost]
        [Route("{storeId}/wallet/{cryptoCode}/import/{method}")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> ImportWallet(string storeId, string cryptoCode, [FromForm] ImportWalletViewModel vm)
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
                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = "Config file was not in the correct format"
                    });
                    vm.Confirmation = false;
                    return View(nameof(AddDerivationScheme), vm);
                }
            }

            if (vm.WalletFile != null)
            {
                if (!DerivationSchemeSettings.TryParseFromWalletFile(await ReadAllText(vm.WalletFile), network,
                    out strategy))
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = "Wallet file was not in the correct format"
                    });
                    vm.Confirmation = false;
                    return View(nameof(AddDerivationScheme), vm);
                }
            }
            else if (!string.IsNullOrEmpty(vm.WalletFileContent))
            {
                if (!DerivationSchemeSettings.TryParseFromWalletFile(vm.WalletFileContent, network, out strategy))
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = "QR import was not in the correct format"
                    });
                    vm.Confirmation = false;
                    return View(nameof(AddDerivationScheme), vm);
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
                        strategy = null;
                    }
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid Derivation Scheme");
                    vm.Confirmation = false;
                    return View(nameof(AddDerivationScheme), vm);
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
                // - The user is clicking on continue without changing config nor enabling/disabling
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
                    storeBlob.Hints.Wallet = false;
                    store.SetStoreBlob(storeBlob);
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid Derivation Scheme");
                    return View(vm);
                }

                await _Repo.UpdateStore(store);
                _EventAggregator.Publish(new WalletChangedEvent() {WalletId = new WalletId(storeId, cryptoCode)});

                if (willBeExcluded != wasExcluded)
                {
                    var label = willBeExcluded ? "disabled" : "enabled";
                    TempData[WellKnownTempData.SuccessMessage] =
                        $"On-Chain payments for {network.CryptoCode} has been {label}.";
                }
                else
                {
                    TempData[WellKnownTempData.SuccessMessage] =
                        $"Derivation settings for {network.CryptoCode} has been modified.";
                }

                // This is success case when derivation scheme is added to the store
                return RedirectToAction(nameof(UpdateStore), new {storeId = storeId});
            }

            if (!string.IsNullOrEmpty(vm.HintAddress))
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
                TempData[WellKnownTempData.SuccessMessage] =
                    "Address successfully found, please verify that the rest is correct and click on \"Confirm\"";
                ModelState.Remove(nameof(vm.HintAddress));
                ModelState.Remove(nameof(vm.DerivationScheme));
            }

            return ShowAddresses(vm, strategy);
        }

        [HttpGet]
        [Route("{storeId}/wallet/{cryptoCode}/generate")]
        public async Task<IActionResult> GenerateWallet(GenerateWalletViewModel vm)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            var network = vm.CryptoCode == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCode);
            if (network == null)
            {
                return NotFound();
            }

            vm.RootKeyPath = network.GetRootKeyPath();
            vm.Network = network;
            var derivation = GetExistingDerivationStrategy(vm.CryptoCode, store);
            if (derivation != null)
            {
                vm.DerivationScheme = derivation.AccountDerivation.ToString();
                vm.Config = derivation.ToJson();
            }
            vm.Enabled = !store.GetStoreBlob().IsExcluded(new PaymentMethodId(vm.CryptoCode, PaymentTypes.BTCLike));
            var hotWallet = await CanUseHotWallet();
            vm.CanUseHotWallet = hotWallet.HotWallet;
            vm.CanUseRPCImport = hotWallet.RPCImport;
            return View(vm);
        }

        [HttpPost]
        [Route("{storeId}/wallet/{cryptoCode}/generate")]
        public async Task<IActionResult> GenerateWallet(string storeId, string cryptoCode, GenerateWalletRequest request)
        {
            var hotWallet = await CanUseHotWallet();
            if (!hotWallet.HotWallet || (!hotWallet.RPCImport && request.ImportKeysToRPC))
            {
                return NotFound();
            }

            var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            var client = _ExplorerProvider.GetExplorerClient(cryptoCode);
            GenerateWalletResponse response;
            try
            {
                response = await client.GenerateWalletAsync(request);
            }
            catch (Exception e)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = $"There was an error generating your wallet: {e.Message}"
                });
                return RedirectToAction(nameof(AddDerivationScheme), new {storeId, cryptoCode});
            }

            if (response == null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = "There was an error generating your wallet. Is your node available?"
                });
                return RedirectToAction(nameof(AddDerivationScheme), new {storeId, cryptoCode});
            }

            var store = HttpContext.GetStoreData();
            var result = await AddDerivationScheme(storeId,
                new DerivationSchemeViewModel()
                {
                    Confirmation = string.IsNullOrEmpty(request.ExistingMnemonic),
                    Network = network,
                    RootFingerprint = response.AccountKeyPath.MasterFingerprint.ToString(),
                    RootKeyPath = network.GetRootKeyPath(),
                    CryptoCode = cryptoCode,
                    DerivationScheme = response.DerivationScheme.ToString(),
                    Source = "NBXplorer",
                    AccountKey = response.AccountHDKey.Neuter().ToWif(),
                    DerivationSchemeFormat = "BTCPay",
                    KeyPath = response.AccountKeyPath.KeyPath.ToString(),
                    Enabled = !store.GetStoreBlob()
                        .IsExcluded(new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike))
                }, cryptoCode);
            if (!ModelState.IsValid || !(result is RedirectToActionResult))
                return result;
            TempData.Clear();
            if (string.IsNullOrEmpty(request.ExistingMnemonic))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Html = $"<span class='text-centered'>Your wallet has been generated.</span>"
                });
                var vm = new RecoverySeedBackupViewModel()
                {
                    CryptoCode = cryptoCode,
                    Mnemonic = response.Mnemonic,
                    Passphrase = response.Passphrase,
                    IsStored = request.SavePrivateKeys,
                    ReturnUrl = Url.Action(nameof(UpdateStore), new {storeId})
                };
                return this.RedirectToRecoverySeedBackup(vm);
            }
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Html = "Please check your addresses and confirm"
            });
            return result;
        }
    }
}
