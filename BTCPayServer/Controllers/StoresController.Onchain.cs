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
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace BTCPayServer.Controllers
{
    public partial class StoresController
    {
        [HttpGet("{storeId}/onchain/{cryptoCode}")]
        public ActionResult SetupWallet(WalletSetupViewModel vm)
        {
            var checkResult = IsAvailable(vm.CryptoCode, out _, out _);
            if (checkResult != null)
            {
                return checkResult;
            }

            var derivation = GetExistingDerivationStrategy(vm.CryptoCode);
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
            else if (!string.IsNullOrEmpty(vm.DerivationScheme))
            {
                try
                {
                    strategy = ParseDerivationStrategy(vm.DerivationScheme, null, network);
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid wallet format");
                    return View(vm.ViewName, vm);
                }
            }

            if (strategy is null)
            {
                ModelState.AddModelError(nameof(vm.DerivationScheme), "Please provide your extended public key");
                return View(vm.ViewName, vm);
            }

            if (!vm.Confirmation)
            {
                return ConfirmAddresses(vm, strategy);
            }
            else
            {
                try
                {
                    await UpdateStoreDerivationSchemeSettings(network, strategy);
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid derivation scheme");
                    return View(vm.ViewName, vm);
                }

                _EventAggregator.Publish(new WalletChangedEvent {WalletId = new WalletId(vm.StoreId, vm.CryptoCode)});

                TempData[WellKnownTempData.SuccessMessage] = $"Derivation settings for {network.CryptoCode} have been updated.";

                // This is success case when derivation scheme is added to the store
                return RedirectToAction(nameof(UpdateStore), new {storeId = vm.StoreId});
            }
        }

        private async Task UpdateStoreDerivationSchemeSettings(BTCPayNetwork network, DerivationSchemeSettings strategy)
        {
            var store = HttpContext.GetStoreData();
            var wallet = _WalletProvider.GetWallet(network);
            if (strategy != null)
                await wallet.TrackAsync(strategy.AccountDerivation);
            var storeBlob = store.GetStoreBlob();
            PaymentMethodId paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
            store.SetSupportedPaymentMethod(paymentMethodId, strategy);
            storeBlob.Hints.Wallet = false;
            store.SetStoreBlob(storeBlob);
            await _Repo.UpdateStore(store);
        }

        [HttpGet("{storeId}/onchain/{cryptoCode}/generate/{method?}")]
        public async Task<IActionResult> GenerateWallet(WalletSetupViewModel vm)
        {
            var checkResult = IsAvailable(vm.CryptoCode, out _, out var network);
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

            var derivation = GetExistingDerivationStrategy(vm.CryptoCode);
            if (derivation != null)
            {
                vm.DerivationScheme = derivation.AccountDerivation.ToString();
            }

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
            var checkResult = IsAvailable(cryptoCode, out _, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            var (hotWallet, rpcImport) = await CanUseHotWallet();
            if ((!hotWallet && request.SavePrivateKeys) || (!rpcImport && request.ImportKeysToRPC))
            {
                return NotFound();
            }

            var walletSetup = new WalletSetupViewModel()
            {
                Method = method,
                CanUseHotWallet = hotWallet,
                CanUseRPCImport = rpcImport,
                SetupRequest = request
            };
            var client = _ExplorerProvider.GetExplorerClient(cryptoCode);
            if (method == WalletSetupMethod.Seed && string.IsNullOrEmpty(request.ExistingMnemonic))
            {
                ModelState.AddModelError(nameof(request.ExistingMnemonic), "Please provide your existing seed");
                return View(WalletSetupViewModel.GetViewName(method), walletSetup);
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
                return View(WalletSetupViewModel.GetViewName(method), walletSetup);
            }

            var derivationSchemeSettings = new DerivationSchemeSettings(response.DerivationScheme, network);
            if (method == WalletSetupMethod.Seed)
            {
                derivationSchemeSettings.Source = "ImportedSeed";
                derivationSchemeSettings.IsHotWallet = request.SavePrivateKeys;
            }
            else
            {
                derivationSchemeSettings.Source = "NBXplorerGenerated";
                derivationSchemeSettings.IsHotWallet = method == WalletSetupMethod.HotWallet;
            }

            var accountSettings = derivationSchemeSettings.GetSigningAccountKeySettings();
            accountSettings.AccountKeyPath = response.AccountKeyPath.KeyPath;
            accountSettings.RootFingerprint = response.AccountKeyPath.MasterFingerprint;
            derivationSchemeSettings.AccountOriginal = response.DerivationScheme.ToString();



            await UpdateStoreDerivationSchemeSettings(network, derivationSchemeSettings);

            if (method == WalletSetupMethod.Seed)
            {
                TempData[WellKnownTempData.SuccessMessage] = $"Derivation settings for {network.CryptoCode} have been updated.";
                return RedirectToAction(nameof(UpdateStore), new { storeId = storeId });
            }
            else
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
                    ReturnUrl = Url.Action(nameof(GenerateWalletConfirm), new { storeId, cryptoCode })
                };
                return this.RedirectToRecoverySeedBackup(seedVm);
            }
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

            TempData[WellKnownTempData.SuccessMessage] = $"Derivation settings for {network.CryptoCode} have been updated.";

            return RedirectToAction(nameof(UpdateStore), new {storeId});
        }

        [HttpGet("{storeId}/onchain/{cryptoCode}/modify")]
        public async Task<IActionResult> ModifyWallet(WalletSetupViewModel vm)
        {
            var checkResult = IsAvailable(vm.CryptoCode, out _, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            var derivation = GetExistingDerivationStrategy(vm.CryptoCode);
            if (derivation == null)
            {
                return NotFound();
            }

            var (hotWallet, rpcImport) = await CanUseHotWallet();
            var isHotWallet = await IsHotWallet(vm.CryptoCode, derivation);

            vm.CanUseHotWallet = hotWallet;
            vm.CanUseRPCImport = rpcImport;
            vm.RootKeyPath = network.GetRootKeyPath();
            vm.Network = network;
            vm.Source = derivation.Source;
            vm.RootFingerprint = derivation.GetSigningAccountKeySettings().RootFingerprint.ToString();
            vm.DerivationScheme = derivation.AccountDerivation.ToString();
            vm.KeyPath = derivation.GetSigningAccountKeySettings().AccountKeyPath?.ToString();
            vm.IsHotWallet = isHotWallet;

            return View(vm);
        }

        [HttpGet("{storeId}/onchain/{cryptoCode}/replace")]
        public async Task<IActionResult> ReplaceWallet(string storeId, string cryptoCode)
        {
            var checkResult = IsAvailable(cryptoCode, out _, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            var derivation = GetExistingDerivationStrategy(cryptoCode);
            var isHotWallet = await IsHotWallet(cryptoCode, derivation);
            var walletType = isHotWallet ? "hot" : "watch-only";
            var additionalText = isHotWallet
                ? ""
                : " or imported into an external wallet. If you no longer have access to your private key (recovery seed), immediately replace the wallet";
            var description =
                $"<p class=\"text-danger fw-bold\">Please note that this is a {walletType} wallet!</p>" +
                $"<p class=\"text-danger fw-bold\">Do not replace the wallet if you have not backed it up{additionalText}.</p>" +
                "<p class=\"text-start mb-0\">Replacing the wallet will erase the current wallet data from the server. " +
                "The current wallet will be replaced once you finish the setup of the new wallet. If you cancel the setup, the current wallet will stay active  .</p>";

            return View("Confirm", new ConfirmModel
            {
                Title = $"Replace {network.CryptoCode} wallet",
                Description = description,
                DescriptionHtml = true,
                Action = "Setup new wallet"
            });
        }

        [HttpPost("{storeId}/onchain/{cryptoCode}/replace")]
        public IActionResult ConfirmReplaceWallet(string storeId, string cryptoCode)
        {
            var checkResult = IsAvailable(cryptoCode, out _, out _);
            if (checkResult != null)
            {
                return checkResult;
            }

            var derivation = GetExistingDerivationStrategy(cryptoCode);
            if (derivation == null)
            {
                return NotFound();
            }

            return RedirectToAction(nameof(SetupWallet), new {storeId, cryptoCode});
        }

        [HttpGet("{storeId}/onchain/{cryptoCode}/delete")]
        public async Task<IActionResult> DeleteWallet(string storeId, string cryptoCode)
        {
            var checkResult = IsAvailable(cryptoCode, out _, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            var derivation = GetExistingDerivationStrategy(cryptoCode);
            var isHotWallet = await IsHotWallet(cryptoCode, derivation);
            var walletType = isHotWallet ? "hot" : "watch-only";
            var additionalText = isHotWallet
                ? ""
                : " or imported into an external wallet. If you no longer have access to your private key (recovery seed), immediately replace the wallet";
            var description =
                $"<p class=\"text-danger fw-bold\">Please note that this is a {walletType} wallet!</p>" +
                $"<p class=\"text-danger fw-bold\">Do not remove the wallet if you have not backed it up{additionalText}.</p>" +
                "<p class=\"text-start mb-0\">Removing the wallet will erase the wallet data from the server. " +
                $"The store won't be able to receive {network.CryptoCode} onchain payments until a new wallet is set up.</p>";

            return View("Confirm", new ConfirmModel
            {
                Title = $"Remove {network.CryptoCode} wallet",
                Description = description,
                DescriptionHtml = true,
                Action = "Remove"
            });
        }

        [HttpPost("{storeId}/onchain/{cryptoCode}/status")]
        public async Task<IActionResult> SetWalletEnabled(string storeId, string cryptoCode, bool enabled)
        {
            var checkResult = IsAvailable(cryptoCode, out var store, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            var derivation = GetExistingDerivationStrategy(cryptoCode);
            if (derivation == null)
            {
                return NotFound();
            }

            var wallet = _WalletProvider.GetWallet(network);
            if (wallet == null)
            {
                return NotFound();
            }

            var paymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);
            var storeBlob = store.GetStoreBlob();
            storeBlob.SetExcluded(paymentMethodId, !enabled);
            store.SetStoreBlob(storeBlob);
            await _Repo.UpdateStore(store);
            _EventAggregator.Publish(new WalletChangedEvent {WalletId = new WalletId(storeId, cryptoCode)});

            TempData[WellKnownTempData.SuccessMessage] =
                $"{network.CryptoCode} on-chain payments are now {(enabled ? "enabled" : "disabled")} for this store.";

            return RedirectToAction(nameof(UpdateStore), new {storeId});
        }

        [HttpPost("{storeId}/onchain/{cryptoCode}/delete")]
        public async Task<IActionResult> ConfirmDeleteWallet(string storeId, string cryptoCode)
        {
            var checkResult = IsAvailable(cryptoCode, out var store, out var network);
            if (checkResult != null)
            {
                return checkResult;
            }

            var derivation = GetExistingDerivationStrategy(cryptoCode);
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
            var deposit = new KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);

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

            return View("ImportWallet/ConfirmAddresses", vm);
        }

        private ActionResult IsAvailable(string cryptoCode, out StoreData store, out BTCPayNetwork network)
        {
            store = HttpContext.GetStoreData();
            network = cryptoCode == null ? null : _ExplorerProvider.GetNetwork(cryptoCode);

            return store == null || network == null ? NotFound() : null;
        }

        private DerivationSchemeSettings GetExistingDerivationStrategy(string cryptoCode)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike);
            var existing = HttpContext.GetStoreData().GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<DerivationSchemeSettings>()
                .FirstOrDefault(d => d.PaymentId == id);
            return existing;
        }

        private async Task<(bool HotWallet, bool RPCImport)> CanUseHotWallet()
        {
            var policies = await _settingsRepository.GetSettingAsync<PoliciesSettings>();
            return await _authorizationService.CanUseHotWallet(policies, User);
        }

        private async Task<string> ReadAllText(IFormFile file)
        {
            using (var stream = new StreamReader(file.OpenReadStream()))
            {
                return await stream.ReadToEndAsync();
            }
        }

        private async Task<bool> IsHotWallet(string cryptoCode, DerivationSchemeSettings derivation)
        {
            return derivation.IsHotWallet && await _ExplorerProvider.GetExplorerClient(cryptoCode)
                .GetMetadataAsync<string>(derivation.AccountDerivation, WellknownMetadataKeys.MasterHDKey) != null;
        }
    }
}
