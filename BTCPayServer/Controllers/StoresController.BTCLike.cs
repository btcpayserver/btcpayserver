using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet]
        [Route("{storeId}/derivations/{cryptoCode}")]
        public async Task<IActionResult> AddDerivationScheme(string storeId, string cryptoCode)
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
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> AddDerivationScheme(string storeId, [FromForm] DerivationSchemeViewModel vm,
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
                TempData[WellKnownTempData.SuccessMessage] =
                    "Address successfully found, please verify that the rest is correct and click on \"Confirm\"";
                ModelState.Remove(nameof(vm.HintAddress));
                ModelState.Remove(nameof(vm.DerivationScheme));
            }

            return ShowAddresses(vm, strategy);
        }

        [HttpPost]
        [Route("{storeId}/derivations/{cryptoCode}/generatenbxwallet")]
        public async Task<IActionResult> GenerateNBXWallet(string storeId, string cryptoCode,
            GenerateWalletRequest request)
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
            else
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Warning,
                    Html = "Please check your addresses and confirm"
                });
            }
            return result;
        }

        private async Task<(bool HotWallet, bool RPCImport)> CanUseHotWallet()
        {
            var isAdmin = (await _authorizationService.AuthorizeAsync(User, Policies.CanModifyServerSettings))
                .Succeeded;
            if (isAdmin)
                return (true, true);
            var policies = await _settingsRepository.GetSettingAsync<PoliciesSettings>();
            var hotWallet = policies?.AllowHotWalletForAll is true;
            return (hotWallet, hotWallet && policies?.AllowHotWalletRPCImportForAll is true);
        }

        private async Task<string> ReadAllText(IFormFile file)
        {
            using (var stream = new StreamReader(file.OpenReadStream()))
            {
                return await stream.ReadToEndAsync();
            }
        }

        private IActionResult
            ShowAddresses(DerivationSchemeViewModel vm, DerivationSchemeSettings strategy)
        {
            vm.DerivationScheme = strategy.AccountDerivation.ToString();
            var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);
            if (!string.IsNullOrEmpty(vm.DerivationScheme))
            {
                var line = strategy.AccountDerivation.GetLineFor(deposit);

                for (int i = 0; i < 10; i++)
                {
                    var keyPath = deposit.GetKeyPath((uint)i);
                    var rootedKeyPath = vm.GetAccountKeypath()?.Derive(keyPath);
                    var derivation = line.Derive((uint)i);
                    var address = strategy.Network.NBXplorerNetwork.CreateAddress(strategy.AccountDerivation,
                        line.KeyPathTemplate.GetKeyPath((uint)i),
                        derivation.ScriptPubKey).ToString();
                    vm.AddressSamples.Add((keyPath.ToString(), address, rootedKeyPath));
                }
            }
            vm.Confirmation = true;
            ModelState.Remove(nameof(vm.Config)); // Remove the cached value
            return View(nameof(AddDerivationScheme), vm);
        }
    }
}
