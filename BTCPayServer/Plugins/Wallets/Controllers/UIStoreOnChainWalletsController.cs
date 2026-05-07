using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers;

[Route("stores")]
[Area(WalletsPlugin.Area)]
[Authorize(Policy = WalletPolicies.CanManageWalletSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIStoreOnChainWalletsController : Controller
{
    public UIStoreOnChainWalletsController(
        BTCPayServerEnvironment btcpayEnv,
        StoreRepository storeRepo,
        BTCPayWalletProvider walletProvider,
        ExplorerClientProvider explorerProvider,
        PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
        PoliciesSettings policiesSettings,
        IAuthorizationService authorizationService,
        IDataProtectionProvider dataProtector,
        WalletFileParsers onChainWalletParsers,
        EventAggregator eventAggregator,
        IHtmlHelper html,
        IStringLocalizer stringLocalizer)
    {
        _btcPayEnv = btcpayEnv;
        _storeRepo = storeRepo;
        _walletProvider = walletProvider;
        _explorerProvider = explorerProvider;
        _handlers = paymentMethodHandlerDictionary;
        _policiesSettings = policiesSettings;
        _authorizationService = authorizationService;
        _dataProtector = dataProtector.CreateProtector("ConfigProtector");
        _onChainWalletParsers = onChainWalletParsers;
        _eventAggregator = eventAggregator;
        _html = html;
        StringLocalizer = stringLocalizer;
    }

    private readonly BTCPayServerEnvironment _btcPayEnv;
    private readonly StoreRepository _storeRepo;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly ExplorerClientProvider _explorerProvider;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly PoliciesSettings _policiesSettings;
    private readonly IAuthorizationService _authorizationService;
    private readonly IDataProtector _dataProtector;
    private readonly WalletFileParsers _onChainWalletParsers;
    private readonly EventAggregator _eventAggregator;
    private readonly IHtmlHelper _html;

    public IStringLocalizer StringLocalizer { get; }

    [HttpGet("{storeId}/onchain/{cryptoCode}")]
    public async Task<IActionResult> SetupWallet(
        WalletSetupViewModel vm,
        [FromRoute] string storeId = null,
        [FromRoute] string cryptoCode = null)
    {
        vm.StoreId = storeId ?? vm.StoreId;
        vm.CryptoCode = cryptoCode ?? vm.CryptoCode;

        var checkResult = IsAvailable(vm.CryptoCode, out var store, out _);
        if (checkResult != null)
        {
            return checkResult;
        }

        var derivation = GetExistingDerivationStrategy(vm.CryptoCode, store);
        vm.DerivationScheme = derivation?.AccountDerivation.ToString();

        var perm = await CanUseHotWallet();
        var canAccessSeedMaterial =
            (await _authorizationService.AuthorizeAsync(User, vm.StoreId, Policies.CanModifyStoreSettings)).Succeeded;
        vm.SetPermission(perm);
        vm.CanGenerateNewWallet = canAccessSeedMaterial && (vm.CanUseHotWallet || vm.CanCreateNewColdWallet);

        return View(nameof(SetupWallet), vm);
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/import")]
    [HttpGet("{storeId}/onchain/{cryptoCode}/import/{method:regex(^(hardware|file|xpub|scan|seed)$)}")]
    public async Task<IActionResult> ImportWallet(
        WalletSetupViewModel vm,
        [FromRoute] string storeId = null,
        [FromRoute] string cryptoCode = null,
        [FromRoute] WalletSetupMethod? method = null)
    {
        vm.StoreId = storeId ?? vm.StoreId;
        vm.CryptoCode = cryptoCode ?? vm.CryptoCode;
        vm.Method = method ?? vm.Method;

        var checkResult = IsAvailable(vm.CryptoCode, out _, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }
        if (vm.Method == WalletSetupMethod.Seed &&
            !(await _authorizationService.AuthorizeAsync(User, vm.StoreId, Policies.CanModifyStoreSettings)).Succeeded)
            return Forbid();

        var perm = await CanUseHotWallet();
        vm.Network = network;
        vm.SetPermission(perm);
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
    [HttpPost("{storeId}/onchain/{cryptoCode}/import/{method:regex(^(hardware|file|xpub|scan|seed)$)}")]
    public async Task<IActionResult> UpdateWallet(
        WalletSetupViewModel vm,
        [FromRoute] string storeId = null,
        [FromRoute] string cryptoCode = null)
    {
        vm.StoreId = storeId ?? vm.StoreId;
        vm.CryptoCode = cryptoCode ?? vm.CryptoCode;

        return await UpdateWalletCore(vm);
    }

    private async Task<IActionResult> UpdateWalletCore(WalletSetupViewModel vm)
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
                ModelState.AddModelError(nameof(vm.WalletFile), StringLocalizer["Import failed, make sure you import a compatible wallet format"]);
                return View(vm.ViewName, vm);
            }
        }
        else if (!string.IsNullOrEmpty(vm.WalletFileContent))
        {
            if (!_onChainWalletParsers.TryParseWalletFile(vm.WalletFileContent, network, out strategy, out var error))
            {
                ModelState.AddModelError(nameof(vm.WalletFileContent), StringLocalizer["QR import failed: {0}", error]);
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
                ModelState.AddModelError(nameof(vm.DerivationScheme), StringLocalizer["Invalid wallet format: {0}", ex.Message]);
                return View(vm.ViewName, vm);
            }
        }
        else if (!string.IsNullOrEmpty(vm.Config))
        {
            try
            {
                strategy = handler.ParsePaymentMethodConfig(JToken.Parse(_dataProtector.UnprotectString(vm.Config)));
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.Config), StringLocalizer["Config file was not in the correct format"]);
                return View(vm.ViewName, vm);
            }
        }

        if (strategy is null)
        {
            ModelState.AddModelError(nameof(vm.DerivationScheme), StringLocalizer["Please provide your extended public key"]);
            return View(vm.ViewName, vm);
        }
        vm.Config = _dataProtector.ProtectString(JToken.FromObject(strategy, handler.Serializer).ToString());
        ModelState.Remove(nameof(vm.Config));

        if (vm.Confirmation)
        {
            try
            {
                await wallet.TrackAsync(strategy.AccountDerivation);
                store.SetPaymentMethodConfig(_handlers[paymentMethodId], strategy);
                var storeBlob = store.GetStoreBlob();
                storeBlob.SetExcluded(paymentMethodId, false);
                storeBlob.PayJoinEnabled = strategy.IsHotWallet && !(vm.SetupRequest?.PayJoinEnabled is false);
                store.SetStoreBlob(storeBlob);
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.DerivationScheme), StringLocalizer["NBXplorer is unable to track this derivation scheme. You may need to update it."]);
                return View(vm.ViewName, vm);
            }

            await _storeRepo.UpdateStore(store);
            _eventAggregator.Publish(new WalletChangedEvent { WalletId = new WalletId(store.Id, network.CryptoCode) });
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Wallet settings for {0} have been updated.", network.CryptoCode].Value;

            // This is success case when derivation scheme is added to the store
            return RedirectToAction(nameof(WalletSettings), new { storeId = vm.StoreId, cryptoCode = vm.CryptoCode });
        }
        return ConfirmAddresses(vm, strategy, network);
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/generate/{method?}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> GenerateWallet(
        WalletSetupViewModel vm,
        [FromRoute] string storeId = null,
        [FromRoute] string cryptoCode = null,
        [FromRoute] WalletSetupMethod? method = null)
    {
        vm.StoreId = storeId ?? vm.StoreId;
        vm.CryptoCode = cryptoCode ?? vm.CryptoCode;
        vm.Method = method ?? vm.Method;

        var checkResult = IsAvailable(vm.CryptoCode, out _, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var isHotWallet = vm.Method == WalletSetupMethod.HotWallet;
        var isColdWallet = vm.Method == WalletSetupMethod.WatchOnly;
        var perm = await CanUseHotWallet();
        if (isHotWallet && !perm.CanCreateHotWallet || isColdWallet && !perm.CanCreateColdWallet)
            return NotFound();
        vm.SetPermission(perm);
        vm.CanGenerateNewWallet = true;
        vm.SupportTaproot = network.NBitcoinNetwork.Consensus.SupportTaproot;
        vm.SupportSegwit = network.NBitcoinNetwork.Consensus.SupportSegwit;
        vm.Network = network;

        if (vm.Method == null)
        {
            vm.Method = WalletSetupMethod.GenerateOptions;
        }
        else
        {
            var canUsePayJoin = perm.CanCreateHotWallet && isHotWallet && network.SupportPayJoin;
            vm.SetupRequest = new WalletSetupRequest
            {
                SavePrivateKeys = isHotWallet,
                CanUsePayJoin = canUsePayJoin,
                PayJoinEnabled = canUsePayJoin
            };
        }

        return View(vm.ViewName, vm);
    }

    public GenerateWalletResponse GenerateWalletResponse { get; private set; }

    [HttpPost("{storeId}/onchain/{cryptoCode}/generate/{method}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> GenerateWallet(
        [FromRoute] string storeId,
        [FromRoute] string cryptoCode,
        [FromRoute] WalletSetupMethod method,
        WalletSetupRequest request)
    {
        var checkResult = IsAvailable(cryptoCode, out _, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var perm = await CanUseHotWallet();
        if ((!perm.CanCreateHotWallet && request.SavePrivateKeys) ||
            (!perm.CanCreateColdWallet && !request.SavePrivateKeys))
        {
            return NotFound();
        }
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
            SupportTaproot = network.NBitcoinNetwork.Consensus.SupportTaproot,
            SupportSegwit = network.NBitcoinNetwork.Consensus.SupportSegwit
        };
        vm.SetPermission(perm);
        vm.CanGenerateNewWallet = !isImport;
        if (isImport && string.IsNullOrEmpty(request.ExistingMnemonic))
        {
            ModelState.AddModelError(nameof(request.ExistingMnemonic), StringLocalizer["Please provide your existing seed"]);
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
                Message = StringLocalizer["There was an error generating your wallet: {0}", e.Message].Value
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

        var accountSettings = derivationSchemeSettings.AccountKeySettings[0];
        accountSettings.AccountKeyPath = response.AccountKeyPath.KeyPath;
        accountSettings.RootFingerprint = response.AccountKeyPath.MasterFingerprint;
        derivationSchemeSettings.AccountOriginal = response.DerivationScheme.ToString();

        // Set wallet properties from generate response
        vm.RootFingerprint = response.AccountKeyPath.MasterFingerprint.ToString();
        vm.AccountKey = response.AccountHDKey.Neuter().ToWif();
        vm.KeyPath = response.AccountKeyPath.KeyPath.ToString();
        var handler = _handlers.GetBitcoinHandler(cryptoCode);
        vm.Config = _dataProtector.ProtectString(JToken.FromObject(derivationSchemeSettings, handler.Serializer).ToString());

        var result = await UpdateWalletCore(vm);

        if (!ModelState.IsValid || result is not RedirectToActionResult)
            return result;

        if (!isImport)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Html = "<span class='text-centered'>" + StringLocalizer["Your wallet has been generated."].Value + "</span>"
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
            Message = StringLocalizer["Please check your addresses and confirm."].Value
        });
        return result;
    }

    // The purpose of this action is to show the user a success message, which confirms
    // that the store settings have been updated after generating a new wallet.
    [HttpGet("{storeId}/onchain/{cryptoCode}/generate/confirm")]
    public async Task<ActionResult> GenerateWalletConfirm([FromRoute] string storeId, [FromRoute] string cryptoCode)
    {
        var checkResult = IsAvailable(cryptoCode, out _, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Wallet settings for {0} have been updated.", network.CryptoCode].Value;

        var walletId = new WalletId(storeId, cryptoCode);
        return RedirectToAction(nameof(UIWalletsController.WalletTransactions), "UIWallets", new { walletId });
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/settings")]
    public async Task<IActionResult> WalletSettings([FromRoute] string storeId, [FromRoute] string cryptoCode)
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
        var perm = await CanUseHotWallet();
        var canAccessSeedMaterial =
            (await _authorizationService.AuthorizeAsync(User, storeId, Policies.CanModifyStoreSettings)).Succeeded;
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
            RootFingerprint = derivation.GetFirstAccountKeySettings().RootFingerprint.ToString(),
            DerivationScheme = derivation.AccountDerivation?.ToString(),
            DerivationSchemeInput = derivation.AccountOriginal,
            KeyPath = derivation.GetFirstAccountKeySettings().AccountKeyPath?.ToString(),
            UriScheme = network.NBitcoinNetwork.UriScheme,
            Label = derivation.Label,
            NBXSeedAvailable = canAccessSeedMaterial &&
                               derivation.IsHotWallet &&
                               perm.CanCreateHotWallet &&
                               !string.IsNullOrEmpty(await client.GetMetadataAsync<string>(derivation.AccountDerivation,
                                   WellknownMetadataKeys.MasterHDKey)),
            AccountKeys = (derivation.AccountKeySettings ?? [])
                .Select(e => new WalletSettingsAccountKeyViewModel
                {
                    AccountKey = e.AccountKey.ToString(),
                    MasterFingerprint = e.RootFingerprint is { } fp ? fp.ToString() : null,
                    AccountKeyPath = e.AccountKeyPath == null ? "" : $"m/{e.AccountKeyPath}"
                }).ToList(),
            Config = _dataProtector.ProtectString(JToken.FromObject(derivation, handler.Serializer).ToString()),
            PayJoinEnabled = storeBlob.PayJoinEnabled,
            CanUsePayJoin = perm.CanCreateHotWallet && network.SupportPayJoin && derivation.IsHotWallet,
            CanUseHotWallet = perm.CanCreateHotWallet,
            StoreName = store.StoreName,
            CanSetupMultiSig = (derivation.AccountKeySettings ?? []).Length > 1,
            IsMultiSigOnServer = derivation.IsMultiSigOnServer,
            DefaultIncludeNonWitnessUtxo = derivation.DefaultIncludeNonWitnessUtxo
        };

        ViewData["ReplaceDescription"] = WalletReplaceWarning(derivation.IsHotWallet);
        ViewData["RemoveDescription"] = WalletRemoveWarning(derivation.IsHotWallet, network.CryptoCode);

        return View(nameof(WalletSettings), vm);
    }
    [HttpPost("{storeId}/onchain/{cryptoCode}/settings/wallet")]
    public async Task<IActionResult> UpdateWalletSettings(
        WalletSettingsViewModel vm,
        [FromRoute] string storeId = null,
        [FromRoute] string cryptoCode = null)
    {
        vm.StoreId = storeId ?? vm.StoreId;
        vm.CryptoCode = cryptoCode ?? vm.CryptoCode;

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

        if (derivation.Label != vm.Label ||
            derivation.IsMultiSigOnServer != vm.IsMultiSigOnServer ||
            derivation.DefaultIncludeNonWitnessUtxo != vm.DefaultIncludeNonWitnessUtxo)
        {
            needUpdate = true;
            derivation.Label = vm.Label;
            derivation.IsMultiSigOnServer = vm.IsMultiSigOnServer;
            derivation.DefaultIncludeNonWitnessUtxo = vm.DefaultIncludeNonWitnessUtxo;
        }

        for (int i = 0; i < derivation.AccountKeySettings.Length; i++)
        {
            try
            {
                var strKeyPath = vm.AccountKeys[i].AccountKeyPath;
                var accountKeyPath = string.IsNullOrWhiteSpace(strKeyPath) ? null : new KeyPath(strKeyPath);

                bool pathsDiffer = accountKeyPath != derivation.AccountKeySettings[i].AccountKeyPath;

                if (pathsDiffer)
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
                HDFingerprint? rootFingerprint = string.IsNullOrWhiteSpace(vm.AccountKeys[i].MasterFingerprint)
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
    public async Task<IActionResult> WalletSeed(
        [FromRoute] string storeId,
        [FromRoute] string cryptoCode,
        CancellationToken cancellationToken = default)
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

        if (!(await CanUseHotWallet()).CanCreateHotWallet)
            return NotFound();

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
            Message = StringLocalizer["The seed was not found"].Value
        });

        return RedirectToAction(nameof(WalletSettings));
    }

    [HttpGet("{storeId}/onchain/{cryptoCode}/replace")]
    public async Task<ActionResult> ReplaceWallet([FromRoute] string storeId, [FromRoute] string cryptoCode)
    {
        var checkResult = IsAvailable(cryptoCode, out var store, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var derivation = GetExistingDerivationStrategy(cryptoCode, store);

        return View("Confirm", new ConfirmModel
        {
            Title = StringLocalizer["Replace {0} wallet", network.CryptoCode],
            Description = WalletReplaceWarning(derivation.IsHotWallet),
            Action = StringLocalizer["Setup new wallet"]
        });
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/replace")]
    public async Task<IActionResult> ConfirmReplaceWallet([FromRoute] string storeId, [FromRoute] string cryptoCode)
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
    public async Task<ActionResult> DeleteWallet([FromRoute] string storeId, [FromRoute] string cryptoCode)
    {
        var checkResult = IsAvailable(cryptoCode, out var store, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var derivation = GetExistingDerivationStrategy(cryptoCode, store);

        return View("Confirm", new ConfirmModel
        {
            Title = StringLocalizer["Remove {0} wallet", network.CryptoCode],
            Description = WalletRemoveWarning(derivation.IsHotWallet, network.CryptoCode),
            Action = StringLocalizer["Delete"]
        });
    }

    [HttpPost("{storeId}/onchain/{cryptoCode}/delete")]
    public async Task<IActionResult> ConfirmDeleteWallet([FromRoute] string storeId, [FromRoute] string cryptoCode)
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

        return RedirectToAction((await _authorizationService.AuthorizeAsync(User, storeId, Policies.CanModifyStoreSettings)).Succeeded ?
            nameof(UIStoresController.GeneralSettings) : nameof(UIStoresController.Index), "UIStores", new { area = "", storeId });
    }

    private IActionResult ConfirmAddresses(WalletSetupViewModel vm, DerivationSchemeSettings strategy, BTCPayNetwork network)
    {
        vm.DerivationScheme = strategy.AccountDerivation.ToString();
        vm.AddressSamples = new();
        if (!string.IsNullOrEmpty(vm.DerivationScheme))
        {
            var result = GreenfieldStoreOnChainPaymentMethodsController.GetPreviewResultData(0, 10, network, strategy.AccountDerivation);
            foreach (var r in result.Addresses)
            {
                vm.AddressSamples.Add((r.KeyPath, r.Address));
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

    private async Task<WalletCreationPermissions> CanUseHotWallet()
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

    public static DerivationSchemeSettings ParseDerivationStrategy(string derivationScheme, BTCPayNetwork network)
    {
        var parser = new DerivationSchemeParser(network);
        var isOD = Regex.Match(derivationScheme, @"\(.*?\)");
        if (isOD.Success)
        {
            return parser.ParseOD(derivationScheme);
        }

        var strategy = parser.Parse(derivationScheme);
        return new DerivationSchemeSettings(strategy, network);
    }
}
