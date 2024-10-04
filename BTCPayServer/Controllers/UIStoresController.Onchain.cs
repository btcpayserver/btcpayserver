using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    [HttpGet("{storeId}/onchain/{cryptoCode}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ImportWallet(WalletSetupViewModel vm)
    {
        var checkResult = IsAvailable(vm.CryptoCode, out _, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var (hotWallet, rpcImport) = await CanUseHotWallet();
        vm.Network = network;
        vm.CanUseHotWallet = hotWallet;
        vm.CanUseRPCImport = rpcImport;
        vm.SupportTaproot = network.NBitcoinNetwork.Consensus.SupportTaproot;
        vm.SupportSegwit = network.NBitcoinNetwork.Consensus.SupportSegwit;

        if (vm.Method == null)
        {
            vm.Method = WalletSetupMethod.ImportOptions;
        }
        else if (vm.Method == WalletSetupMethod.Seed)
        {
            vm.SetupRequest = new WalletSetupRequest();
        }

        return View(vm.ViewName, vm);
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/modify")]
    [HttpPost("{storeId}/onchain/{cryptoCode}/import/{method}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> UpdateWallet(WalletSetupViewModel vm)
    {
        var checkResult = IsAvailable(vm.CryptoCode, out var store, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        vm.Network = network;
        DerivationSchemeSettings strategy = null;
        PaymentMethodId paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
        BitcoinLikePaymentHandler handler = (BitcoinLikePaymentHandler)_handlers[paymentMethodId];
        var wallet = _walletProvider.GetWallet(network);
        if (wallet == null)
        {
            return NotFound();
        }

        if (vm.WalletFile != null)
        {
            string fileContent = null;
            try
            {
                fileContent = await ReadAllText(vm.WalletFile);
            }
            catch
            {
                // ignored
            }

            if (fileContent is null || !_onChainWalletParsers.TryParseWalletFile(fileContent, network, out strategy, out _))
            {
                ModelState.AddModelError(nameof(vm.WalletFile), $"Import failed, make sure you import a compatible wallet format");
                return View(vm.ViewName, vm);
            }
        }
        else if (!string.IsNullOrEmpty(vm.WalletFileContent))
        {
            if (!_onChainWalletParsers.TryParseWalletFile(vm.WalletFileContent, network, out strategy, out var error))
            {
                ModelState.AddModelError(nameof(vm.WalletFileContent), $"QR import failed: {error}");
                return View(vm.ViewName, vm);
            }
        }
        else if (!string.IsNullOrEmpty(vm.DerivationScheme))
        {
            try
            {
                strategy = ParseDerivationStrategy(vm.DerivationScheme, network);
                strategy.Source = "ManualDerivationScheme";
                if (!string.IsNullOrEmpty(vm.AccountKey))
                {
                    var accountKey = new BitcoinExtPubKey(vm.AccountKey, network.NBitcoinNetwork);
                    var accountSettings =
                        strategy.AccountKeySettings.FirstOrDefault(a => a.AccountKey == accountKey);
                    if (accountSettings != null)
                    {
                        accountSettings.AccountKeyPath =
                            vm.KeyPath == null ? null : KeyPath.Parse(vm.KeyPath);
                        accountSettings.RootFingerprint = string.IsNullOrEmpty(vm.RootFingerprint)
                            ? null
                            : new HDFingerprint(Encoders.Hex.DecodeData(vm.RootFingerprint));
                    }
                }
                vm.DerivationScheme = strategy.AccountDerivation.ToString();
                ModelState.Remove(nameof(vm.DerivationScheme));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(nameof(vm.DerivationScheme), $"Invalid wallet format: {ex.Message}");
                return View(vm.ViewName, vm);
            }
        }
        else if (!string.IsNullOrEmpty(vm.Config))
        {
            try
            {
                strategy = handler.ParsePaymentMethodConfig(JToken.Parse(UnprotectString(vm.Config)));
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.Config), "Config file was not in the correct format");
                return View(vm.ViewName, vm);
            }
        }

        if (strategy is null)
        {
            ModelState.AddModelError(nameof(vm.DerivationScheme), "Please provide your extended public key");
            return View(vm.ViewName, vm);
        }

        vm.Config = ProtectString(JToken.FromObject(strategy, handler.Serializer).ToString());
        ModelState.Remove(nameof(vm.Config));

        var storeBlob = store.GetStoreBlob();
        if (vm.Confirmation)
        {
            try
            {
                await wallet.TrackAsync(strategy.AccountDerivation);
                store.SetPaymentMethodConfig(_handlers[paymentMethodId], strategy);
                storeBlob.SetExcluded(paymentMethodId, false);
                storeBlob.PayJoinEnabled = strategy.IsHotWallet && !(vm.SetupRequest?.PayJoinEnabled is false);
                store.SetStoreBlob(storeBlob);
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid derivation scheme");
                return View(vm.ViewName, vm);
            }
            await _storeRepo.UpdateStore(store);
            _eventAggregator.Publish(new WalletChangedEvent { WalletId = new WalletId(vm.StoreId, vm.CryptoCode) });

            TempData[WellKnownTempData.SuccessMessage] = $"Wallet settings for {network.CryptoCode} have been updated.";

            // This is success case when derivation scheme is added to the store
            return RedirectToAction(nameof(WalletSettings), new { storeId = vm.StoreId, cryptoCode = vm.CryptoCode });
        }
        return ConfirmAddresses(vm, strategy, network.NBXplorerNetwork);
    }

    private string ProtectString(string str)
    {
        return Convert.ToBase64String(_dataProtector.Protect(Encoding.UTF8.GetBytes(str)));
    }
    private string UnprotectString(string str)
    {
        return Encoding.UTF8.GetString(_dataProtector.Unprotect(Convert.FromBase64String(str)));
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/generate/{method?}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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

        vm.CanUseHotWallet = hotWallet;
        vm.CanUseRPCImport = rpcImport;
        vm.SupportTaproot = network.NBitcoinNetwork.Consensus.SupportTaproot;
        vm.SupportSegwit = network.NBitcoinNetwork.Consensus.SupportSegwit;
        vm.Network = network;

        if (vm.Method == null)
        {
            vm.Method = WalletSetupMethod.GenerateOptions;
        }
        else
        {
            var canUsePayJoin = hotWallet && isHotWallet && network.SupportPayJoin;
            vm.SetupRequest = new WalletSetupRequest
            {
                SavePrivateKeys = isHotWallet,
                CanUsePayJoin = canUsePayJoin,
                PayJoinEnabled = canUsePayJoin
            };
        }

        return View(vm.ViewName, vm);
    }

    internal GenerateWalletResponse GenerateWalletResponse;

    [HttpPost("{storeId}/onchain/{cryptoCode}/generate/{method}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> GenerateWallet(string storeId, string cryptoCode, WalletSetupMethod method, WalletSetupRequest request)
    {
        var checkResult = IsAvailable(cryptoCode, out _, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var (hotWallet, rpcImport) = await CanUseHotWallet();
        if (!hotWallet && request.SavePrivateKeys || !rpcImport && request.ImportKeysToRPC)
        {
            return NotFound();
        }
        var handler = _handlers.GetBitcoinHandler(cryptoCode);
        var client = _explorerProvider.GetExplorerClient(cryptoCode);
        var isImport = method == WalletSetupMethod.Seed;
        var vm = new WalletSetupViewModel
        {
            StoreId = storeId,
            CryptoCode = cryptoCode,
            Method = method,
            SetupRequest = request,
            Confirmation = !isImport,
            Network = network,
            Source = isImport ? "SeedImported" : "NBXplorerGenerated",
            IsHotWallet = isImport ? request.SavePrivateKeys : method == WalletSetupMethod.HotWallet,
            DerivationSchemeFormat = "BTCPay",
            CanUseHotWallet = hotWallet,
            CanUseRPCImport = rpcImport,
            SupportTaproot = network.NBitcoinNetwork.Consensus.SupportTaproot,
            SupportSegwit = network.NBitcoinNetwork.Consensus.SupportSegwit
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

        // Set wallet properties from generate response
        vm.RootFingerprint = response.AccountKeyPath.MasterFingerprint.ToString();
        vm.AccountKey = response.AccountHDKey.Neuter().ToWif();
        vm.KeyPath = response.AccountKeyPath.KeyPath.ToString();
        vm.Config = ProtectString(JToken.FromObject(derivationSchemeSettings, handler.Serializer).ToString());

        var result = await UpdateWallet(vm);

        if (!ModelState.IsValid || result is not RedirectToActionResult)
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
                ReturnUrl = Url.Action(nameof(GenerateWalletConfirm), new { storeId, cryptoCode })
            };
            if (_btcPayEnv.IsDeveloping)
            {
                GenerateWalletResponse = response;
            }
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
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public ActionResult GenerateWalletConfirm(string storeId, string cryptoCode)
    {
        var checkResult = IsAvailable(cryptoCode, out _, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        TempData[WellKnownTempData.SuccessMessage] = $"Wallet settings for {network.CryptoCode} have been updated.";

        var walletId = new WalletId(storeId, cryptoCode);
        return RedirectToAction(nameof(UIWalletsController.WalletTransactions), "UIWallets", new { walletId });
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> WalletSettings(string storeId, string cryptoCode)
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

        var storeBlob = store.GetStoreBlob();
        var excludeFilters = storeBlob.GetExcludedPaymentMethods();
        (bool canUseHotWallet, bool rpcImport) = await CanUseHotWallet();
        var client = _explorerProvider.GetExplorerClient(network);

        var handler = _handlers.GetBitcoinHandler(cryptoCode);
        var vm = new WalletSettingsViewModel
        {
            StoreId = storeId,
            CryptoCode = cryptoCode,
            WalletId = new WalletId(storeId, cryptoCode),
            Enabled = !excludeFilters.Match(handler.PaymentMethodId),
            Network = network,
            IsHotWallet = derivation.IsHotWallet,
            Source = derivation.Source,
            RootFingerprint = derivation.GetSigningAccountKeySettings().RootFingerprint.ToString(),
            DerivationScheme = derivation.AccountDerivation.ToString(),
            DerivationSchemeInput = derivation.AccountOriginal,
            KeyPath = derivation.GetSigningAccountKeySettings().AccountKeyPath?.ToString(),
            UriScheme = network.NBitcoinNetwork.UriScheme,
            Label = derivation.Label,
            SelectedSigningKey = derivation.SigningKey.ToString(),
            NBXSeedAvailable = derivation.IsHotWallet &&
                               canUseHotWallet &&
                               !string.IsNullOrEmpty(await client.GetMetadataAsync<string>(derivation.AccountDerivation,
                                   WellknownMetadataKeys.MasterHDKey)),
            AccountKeys = derivation.AccountKeySettings
                .Select(e => new WalletSettingsAccountKeyViewModel
                {
                    AccountKey = e.AccountKey.ToString(),
                    MasterFingerprint = e.RootFingerprint is { } fp ? fp.ToString() : null,
                    AccountKeyPath = e.AccountKeyPath == null ? "" : $"m/{e.AccountKeyPath}"
                }).ToList(),
            Config = ProtectString(JToken.FromObject(derivation, handler.Serializer).ToString()),
            PayJoinEnabled = storeBlob.PayJoinEnabled,
            CanUsePayJoin = canUseHotWallet && network.SupportPayJoin && derivation.IsHotWallet,
            CanUseHotWallet = canUseHotWallet,
            CanUseRPCImport = rpcImport,
            StoreName = store.StoreName
        };

        ViewData["ReplaceDescription"] = WalletReplaceWarning(derivation.IsHotWallet);
        ViewData["RemoveDescription"] = WalletRemoveWarning(derivation.IsHotWallet, network.CryptoCode);

        return View(vm);
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/settings/wallet")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> UpdateWalletSettings(WalletSettingsViewModel vm)
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
        var handler = _handlers.GetBitcoinHandler(vm.CryptoCode);
        var storeBlob = store.GetStoreBlob();
        var excludeFilters = storeBlob.GetExcludedPaymentMethods();
        var currentlyEnabled = !excludeFilters.Match(handler.PaymentMethodId);
        var enabledChanged = currentlyEnabled != vm.Enabled;
        var payjoinChanged = storeBlob.PayJoinEnabled != vm.PayJoinEnabled;
        var needUpdate = enabledChanged || payjoinChanged;
        string errorMessage = null;

        if (enabledChanged) storeBlob.SetExcluded(handler.PaymentMethodId, !vm.Enabled);
        if (payjoinChanged && network.SupportPayJoin) storeBlob.PayJoinEnabled = vm.PayJoinEnabled;
        if (needUpdate) store.SetStoreBlob(storeBlob);

        if (derivation.Label != vm.Label)
        {
            needUpdate = true;
            derivation.Label = vm.Label;
        }

        var signingKey = string.IsNullOrEmpty(vm.SelectedSigningKey)
            ? null
            : new BitcoinExtPubKey(vm.SelectedSigningKey, network.NBitcoinNetwork);
        if (derivation.SigningKey != signingKey && signingKey != null)
        {
            needUpdate = true;
            derivation.SigningKey = signingKey;
        }

        for (int i = 0; i < derivation.AccountKeySettings.Length; i++)
        {
            KeyPath accountKeyPath;
            HDFingerprint? rootFingerprint;

            try
            {
                accountKeyPath = string.IsNullOrWhiteSpace(vm.AccountKeys[i].AccountKeyPath)
                    ? null
                    : new KeyPath(vm.AccountKeys[i].AccountKeyPath);

                if (accountKeyPath != null && derivation.AccountKeySettings[i].AccountKeyPath != accountKeyPath)
                {
                    needUpdate = true;
                    derivation.AccountKeySettings[i].AccountKeyPath = accountKeyPath;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"{ex.Message}: {vm.AccountKeys[i].AccountKeyPath}";
            }

            try
            {
                rootFingerprint = string.IsNullOrWhiteSpace(vm.AccountKeys[i].MasterFingerprint)
                    ? null
                    : new HDFingerprint(Encoders.Hex.DecodeData(vm.AccountKeys[i].MasterFingerprint));

                if (rootFingerprint != null && derivation.AccountKeySettings[i].RootFingerprint != rootFingerprint)
                {
                    needUpdate = true;
                    derivation.AccountKeySettings[i].RootFingerprint = rootFingerprint;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"{ex.Message}: {vm.AccountKeys[i].MasterFingerprint}";
            }
        }

        if (needUpdate)
        {
            store.SetPaymentMethodConfig(handler, derivation);

            await _storeRepo.UpdateStore(store);

            if (string.IsNullOrEmpty(errorMessage))
            {
                var successMessage = "Wallet settings successfully updated.";
                if (enabledChanged)
                {
                    _eventAggregator.Publish(new WalletChangedEvent { WalletId = new WalletId(vm.StoreId, vm.CryptoCode) });
                    successMessage += $" {vm.CryptoCode} on-chain payments are now {(vm.Enabled ? "enabled" : "disabled")} for this store.";
                }
                
                if (payjoinChanged && storeBlob.PayJoinEnabled && network.SupportPayJoin)
                {
                    var config = store.GetPaymentMethodConfig<DerivationSchemeSettings>(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode), _handlers);
                    if (config?.IsHotWallet is not true)
                    {
                        successMessage += " However, PayJoin will not work, as this isn't a <a href='https://docs.btcpayserver.org/HotWallet/' class='alert-link' target='_blank'>hot wallet</a>.";
                    }
                }

                TempData[WellKnownTempData.SuccessMessage] = successMessage;
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = errorMessage;
            }
        }

        return RedirectToAction(nameof(WalletSettings), new { vm.StoreId, vm.CryptoCode });
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/seed")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> WalletSeed(string storeId, string cryptoCode, CancellationToken cancellationToken = default)
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

        (bool canUseHotWallet, bool _) = await CanUseHotWallet();
        if (!canUseHotWallet)
        {
            return NotFound();
        }

        var client = _explorerProvider.GetExplorerClient(network);
        if (await GetSeed(client, derivation) != null)
        {
            var mnemonic = await client.GetMetadataAsync<string>(derivation.AccountDerivation,
                WellknownMetadataKeys.Mnemonic, cancellationToken);
            var recoveryVm = new RecoverySeedBackupViewModel
            {
                CryptoCode = cryptoCode,
                Mnemonic = mnemonic,
                IsStored = true,
                RequireConfirm = false,
                ReturnUrl = Url.Action(nameof(WalletSettings), new { storeId, cryptoCode })
            };
            return this.RedirectToRecoverySeedBackup(recoveryVm);
        }

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Error,
            Message = "The seed was not found"
        });

        return RedirectToAction(nameof(WalletSettings));
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/replace")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public ActionResult ReplaceWallet(string storeId, string cryptoCode)
    {
        var checkResult = IsAvailable(cryptoCode, out var store, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var derivation = GetExistingDerivationStrategy(cryptoCode, store);

        return View("Confirm", new ConfirmModel
        {
            Title = $"Replace {network.CryptoCode} wallet",
            Description = WalletReplaceWarning(derivation.IsHotWallet),
            Action = "Setup new wallet"
        });
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/replace")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult ConfirmReplaceWallet(string storeId, string cryptoCode)
    {
        var checkResult = IsAvailable(cryptoCode, out var store, out _);
        if (checkResult != null)
        {
            return checkResult;
        }

        var derivation = GetExistingDerivationStrategy(cryptoCode, store);
        if (derivation == null)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(SetupWallet), new { storeId, cryptoCode });
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public ActionResult DeleteWallet(string storeId, string cryptoCode)
    {
        var checkResult = IsAvailable(cryptoCode, out var store, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var derivation = GetExistingDerivationStrategy(cryptoCode, store);

        return View("Confirm", new ConfirmModel
        {
            Title = $"Remove {network.CryptoCode} wallet",
            Description = WalletRemoveWarning(derivation.IsHotWallet, network.CryptoCode),
            Action = "Remove"
        });
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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

        store.SetPaymentMethodConfig(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode), null);

        await _storeRepo.UpdateStore(store);
        _eventAggregator.Publish(new WalletChangedEvent { WalletId = new WalletId(storeId, cryptoCode) });

        TempData[WellKnownTempData.SuccessMessage] =
            $"On-Chain payment for {network.CryptoCode} has been removed.";

        return RedirectToAction(nameof(GeneralSettings), new { storeId });
    }

    private IActionResult ConfirmAddresses(WalletSetupViewModel vm, DerivationSchemeSettings strategy, NBXplorerNetwork network)
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
                var address = network.CreateAddress(strategy.AccountDerivation,
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
        network = cryptoCode == null ? null : _explorerProvider.GetNetwork(cryptoCode);

        return store == null || network == null ? NotFound() : null;
    }

    private DerivationSchemeSettings GetExistingDerivationStrategy(string cryptoCode, StoreData store)
    {
        return store.GetPaymentMethodConfig<DerivationSchemeSettings>(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode), _handlers);
    }

    private async Task<string> GetSeed(ExplorerClient client, DerivationSchemeSettings derivation)
    {
        return derivation.IsHotWallet &&
               await client.GetMetadataAsync<string>(derivation.AccountDerivation, WellknownMetadataKeys.MasterHDKey) is { } seed &&
               !string.IsNullOrEmpty(seed) ? seed : null;
    }

    private async Task<(bool HotWallet, bool RPCImport)> CanUseHotWallet()
    {
        return await _authorizationService.CanUseHotWallet(_policiesSettings, User);
    }

    private async Task<string> ReadAllText(IFormFile file)
    {
        using var stream = new StreamReader(file.OpenReadStream());
        return await stream.ReadToEndAsync();
    }

    private string WalletWarning(bool isHotWallet, string info)
    {
        var walletType = isHotWallet ? "hot" : "watch-only";
        var additionalText = isHotWallet
            ? ""
            : " or imported it into an external wallet. If you no longer have access to your private key (recovery seed), immediately replace the wallet";
        return
            $"<p class=\"text-danger fw-bold\">Please note that this is a <strong>{_html.Encode(walletType)} wallet</strong>!</p>" +
            $"<p class=\"text-danger fw-bold\">Do not proceed if you have not backed up the wallet{_html.Encode(additionalText)}.</p>" +
            $"<p class=\"text-start mb-0\">This action will erase the current wallet data from the server. {_html.Encode(info)}</p>";
    }

    private string WalletReplaceWarning(bool isHotWallet)
    {
        return WalletWarning(isHotWallet,
            "The current wallet will be replaced once you finish the setup of the new wallet. " +
            "If you cancel the setup, the current wallet will stay active.");
    }

    private string WalletRemoveWarning(bool isHotWallet, string cryptoCode)
    {
        return WalletWarning(isHotWallet,
            $"The store won't be able to receive {cryptoCode} onchain payments until a new wallet is set up.");
    }
    
    private DerivationSchemeSettings ParseDerivationStrategy(string derivationScheme, BTCPayNetwork network)
    {
        var parser = new DerivationSchemeParser(network);
        var isOD = Regex.Match(derivationScheme, @"\(.*?\)");
        if (isOD.Success)
        {
            var derivationSchemeSettings = new DerivationSchemeSettings();
            var result = parser.ParseOutputDescriptor(derivationScheme);
            derivationSchemeSettings.AccountOriginal = derivationScheme.Trim();
            derivationSchemeSettings.AccountDerivation = result.Item1;
            derivationSchemeSettings.AccountKeySettings = result.Item2?.Select((path, i) => new AccountKeySettings()
            {
                RootFingerprint = path?.MasterFingerprint,
                AccountKeyPath = path?.KeyPath,
                AccountKey = result.Item1.GetExtPubKeys().ElementAt(i).GetWif(parser.Network)
            }).ToArray() ?? new AccountKeySettings[result.Item1.GetExtPubKeys().Count()];
            return derivationSchemeSettings;
        }

        var strategy = parser.Parse(derivationScheme);
        return new DerivationSchemeSettings(strategy, network);
    }
}
