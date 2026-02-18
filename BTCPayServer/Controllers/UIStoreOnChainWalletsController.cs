using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
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
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using MimeKit;

namespace BTCPayServer.Controllers;

[Route("stores")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanManageWalletSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
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
        WalletFileParsers onChainWalletParsers,
        EventAggregator eventAggregator,
        IHtmlHelper html,
        IDataProtectionProvider dataProtector,
        IStringLocalizer stringLocalizer,
        EmailSenderFactory emailSenderFactory)
    {
        _btcPayEnv = btcpayEnv;
        _storeRepo = storeRepo;
        _walletProvider = walletProvider;
        _explorerProvider = explorerProvider;
        _handlers = paymentMethodHandlerDictionary;
        _policiesSettings = policiesSettings;
        _authorizationService = authorizationService;
        _onChainWalletParsers = onChainWalletParsers;
        _eventAggregator = eventAggregator;
        _html = html;
        _dataProtector = dataProtector.CreateProtector("ConfigProtector");
        _multisigInviteProtector = dataProtector.CreateProtector("MultisigInviteLink");
        _emailSenderFactory = emailSenderFactory;
        StringLocalizer = stringLocalizer;
    }

    private readonly BTCPayServerEnvironment _btcPayEnv;
    private readonly StoreRepository _storeRepo;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly ExplorerClientProvider _explorerProvider;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly PoliciesSettings _policiesSettings;
    private readonly IAuthorizationService _authorizationService;
    private readonly WalletFileParsers _onChainWalletParsers;
    private readonly EventAggregator _eventAggregator;
    private readonly IHtmlHelper _html;
    private readonly IDataProtector _dataProtector;
    private readonly IDataProtector _multisigInviteProtector;
    private readonly EmailSenderFactory _emailSenderFactory;
    private const string PendingMultisigSettingPrefix = "PendingMultisigSetup";

    public IStringLocalizer StringLocalizer { get; }
        [HttpGet("{storeId}/onchain/{cryptoCode}")]
        public async Task<ActionResult> SetupWallet(WalletSetupViewModel vm)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(vm.StoreId, vm.CryptoCode))
            return Forbid();
        var checkResult = IsAvailable(vm.CryptoCode, out var store, out _);
        if (checkResult != null)
        {
            return checkResult;
        }

        var derivation = GetExistingDerivationStrategy(vm.CryptoCode, store);
        vm.DerivationScheme = derivation?.AccountDerivation.ToString();

        var perm = await CanUseHotWallet();
        vm.SetPermission(perm);

        return StoreView("SetupWallet", vm);
    }
        [HttpGet("{storeId}/onchain/{cryptoCode}/import/{method?}")]
        public async Task<IActionResult> ImportWallet(WalletSetupViewModel vm)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(vm.StoreId, vm.CryptoCode))
            return Forbid();
        var checkResult = IsAvailable(vm.CryptoCode, out _, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var perm = await CanUseHotWallet();
        vm.Network = network;
        vm.SetPermission(perm);
        vm.SupportTaproot = network.NBitcoinNetwork.Consensus.SupportTaproot;
        vm.SupportSegwit = network.NBitcoinNetwork.Consensus.SupportSegwit;

        if (vm.Method == null)
        {
            vm.Method = WalletSetupMethod.ImportOptions;
        }
        else if (vm.Method == WalletSetupMethod.Multisig)
        {
            vm.MultisigScriptType ??= "p2wsh";
            vm.MultisigRequiredSigners ??= 2;
            vm.MultisigTotalSigners ??= 3;
            vm.MultisigSigners ??= Enumerable.Repeat(string.Empty, vm.MultisigTotalSigners.Value).ToArray();
            vm.MultisigSignerFingerprints ??= Enumerable.Repeat(string.Empty, vm.MultisigTotalSigners.Value).ToArray();
            vm.MultisigSignerKeyPaths ??= Enumerable.Repeat(string.Empty, vm.MultisigTotalSigners.Value).ToArray();
            await PopulateMultisigStoreUsers(vm);
            vm.MultisigPendingSetup = string.IsNullOrEmpty(vm.MultisigRequestId)
                ? await GetLatestPendingMultisigSetup(vm.StoreId, vm.CryptoCode)
                : await GetPendingMultisigSetup(vm.StoreId, vm.CryptoCode, vm.MultisigRequestId);
            vm.MultisigRequestId ??= vm.MultisigPendingSetup?.RequestId;
            PopulateMultisigInviteLinks(vm);
        }
        else if (vm.Method == WalletSetupMethod.Seed)
        {
            vm.SetupRequest = new WalletSetupRequest();
        }

        return StoreView(vm.ViewName, vm);
    }
        [HttpPost("{storeId}/onchain/{cryptoCode}/modify")]
        [HttpPost("{storeId}/onchain/{cryptoCode}/import/{method}")]
    public async Task<IActionResult> UpdateWallet(WalletSetupViewModel vm, string command = null)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(vm.StoreId, vm.CryptoCode))
            return Forbid();
        var checkResult = IsAvailable(vm.CryptoCode, out var store, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        vm.Network = network;
        PendingMultisigSetupData finalizedMultisigRequest = null;
        if (vm.Method == WalletSetupMethod.Multisig)
        {
            await PopulateMultisigStoreUsers(vm);
        }

        if (vm.Method == WalletSetupMethod.Multisig && string.Equals(command, "create-request", StringComparison.OrdinalIgnoreCase))
        {
            return await CreateMultisigRequest(vm);
        }

        if (vm.Method == WalletSetupMethod.Multisig && string.Equals(command, "reset-request", StringComparison.OrdinalIgnoreCase))
        {
            await _storeRepo.UpdateSetting<PendingMultisigSetupData>(vm.StoreId, GetPendingMultisigSettingName(vm.CryptoCode), null);
            TempData[WellKnownTempData.SuccessMessage] = "Multisig request was reset.";
            return RedirectToAction(nameof(ImportWallet), new
            {
                storeId = vm.StoreId,
                cryptoCode = vm.CryptoCode,
                method = "multisig"
            });
        }

        if (vm.Method == WalletSetupMethod.Multisig && string.Equals(command, "remove-signer", StringComparison.OrdinalIgnoreCase))
        {
            return await RemoveMultisigSigner(vm);
        }

        if (vm.Method == WalletSetupMethod.Multisig && string.Equals(command, "finalize-request", StringComparison.OrdinalIgnoreCase))
        {
            var pending = await GetPendingMultisigSetup(vm.StoreId, vm.CryptoCode, vm.MultisigRequestId);
            if (pending is null)
            {
                ModelState.AddModelError(nameof(vm.MultisigRequestId), "The multisig request was not found or has expired.");
                return StoreView(vm.ViewName, vm);
            }

            if (pending.Participants.Count != pending.TotalSigners || !pending.Participants.All(p => !string.IsNullOrWhiteSpace(p.AccountKey)))
            {
                ModelState.AddModelError(nameof(vm.MultisigRequestId), "Complete signer collection before creating the multisig wallet.");
                vm.MultisigPendingSetup = pending;
                return StoreView(vm.ViewName, vm);
            }

            vm.MultisigRequiredSigners = pending.RequiredSigners;
            vm.MultisigTotalSigners = pending.TotalSigners;
            vm.MultisigScriptType = pending.ScriptType;
            vm.MultisigSigners = pending.Participants.Select(p => p.AccountKey).ToArray();
            vm.MultisigSignerFingerprints = pending.Participants.Select(p => p.MasterFingerprint ?? string.Empty).ToArray();
            vm.MultisigSignerKeyPaths = pending.Participants.Select(p => p.AccountKeyPath ?? string.Empty).ToArray();
            finalizedMultisigRequest = pending;
        }

        DerivationSchemeSettings strategy = null;
        PaymentMethodId paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
        BitcoinLikePaymentHandler handler = (BitcoinLikePaymentHandler)_handlers[paymentMethodId];
        var wallet = _walletProvider.GetWallet(network);
        if (wallet == null)
        {
            return NotFound();
        }

        if (vm.Method == WalletSetupMethod.Multisig &&
            string.IsNullOrEmpty(vm.Config) &&
            string.IsNullOrEmpty(vm.DerivationScheme) &&
            vm.WalletFile is null &&
            string.IsNullOrEmpty(vm.WalletFileContent))
        {
            if (!TryBuildMultisigDerivationScheme(vm, out var multisigDerivation, out var multisigValidationError))
            {
                ModelState.AddModelError(nameof(vm.DerivationScheme), multisigValidationError);
                return StoreView(vm.ViewName, vm);
            }

            vm.DerivationScheme = multisigDerivation;
            ModelState.Remove(nameof(vm.DerivationScheme));
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
                return StoreView(vm.ViewName, vm);
            }
        }
        else if (!string.IsNullOrEmpty(vm.WalletFileContent))
        {
            if (!_onChainWalletParsers.TryParseWalletFile(vm.WalletFileContent, network, out strategy, out var error))
            {
                ModelState.AddModelError(nameof(vm.WalletFileContent), StringLocalizer["QR import failed: {0}", error]);
                return StoreView(vm.ViewName, vm);
            }
        }
        else if (!string.IsNullOrEmpty(vm.DerivationScheme))
        {
            try
            {
                strategy = ParseDerivationStrategy(vm.DerivationScheme, network);
                strategy.Source = "ManualDerivationScheme";
                if (vm.Method == WalletSetupMethod.Multisig)
                {
                    ApplyMultisigSignerOrigins(vm, strategy);
                }
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
                return StoreView(vm.ViewName, vm);
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
                return StoreView(vm.ViewName, vm);
            }
        }

        if (strategy is null)
        {
            ModelState.AddModelError(nameof(vm.DerivationScheme), StringLocalizer["Please provide your extended public key"]);
            return StoreView(vm.ViewName, vm);
        }

        vm.Config = _dataProtector.ProtectString(JToken.FromObject(strategy, handler.Serializer).ToString());
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
                ModelState.AddModelError(nameof(vm.DerivationScheme), StringLocalizer["NBXplorer is unable to track this derivation scheme. You may need to update it."]);
                return StoreView(vm.ViewName, vm);
            }
            await _storeRepo.UpdateStore(store);
            if (vm.Method == WalletSetupMethod.Multisig && finalizedMultisigRequest is not null)
            {
                await SendMultisigWalletCreatedEmails(vm.StoreId, vm.CryptoCode, finalizedMultisigRequest);
            }
            if (vm.Method == WalletSetupMethod.Multisig && !string.IsNullOrEmpty(vm.MultisigRequestId))
            {
                await _storeRepo.UpdateSetting<PendingMultisigSetupData>(vm.StoreId, GetPendingMultisigSettingName(vm.CryptoCode), null);
            }
            _eventAggregator.Publish(new WalletChangedEvent { WalletId = new WalletId(vm.StoreId, vm.CryptoCode) });

            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Wallet settings for {0} have been updated.", network.CryptoCode].Value;

            // This is success case when derivation scheme is added to the store
            return RedirectToAction(nameof(WalletSettings), new { storeId = vm.StoreId, cryptoCode = vm.CryptoCode });
        }
        return ConfirmAddresses(vm, strategy, network);
    }
        [HttpGet("{storeId}/onchain/{cryptoCode}/generate/{method?}")]
        public async Task<IActionResult> GenerateWallet(WalletSetupViewModel vm)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(vm.StoreId, vm.CryptoCode))
            return Forbid();
        var checkResult = IsAvailable(vm.CryptoCode, out _, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var isHotWallet = vm.Method == WalletSetupMethod.HotWallet;
        var isColdWallet = vm.Method == WalletSetupMethod.WatchOnly;
        var perm = await CanUseHotWallet();
        if (isHotWallet && !perm.CanCreateHotWallet)
            return NotFound();
        if (isColdWallet && !perm.CanCreateColdWallet)
            return NotFound();
        vm.SetPermission(perm);
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

        return StoreView(vm.ViewName, vm);
    }

    internal GenerateWalletResponse GenerateWalletResponse;
        [HttpPost("{storeId}/onchain/{cryptoCode}/generate/{method}")]
        public async Task<IActionResult> GenerateWallet(string storeId, string cryptoCode, WalletSetupMethod method, WalletSetupRequest request)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(storeId, cryptoCode))
            return Forbid();
        var checkResult = IsAvailable(cryptoCode, out _, out var network);
        if (checkResult != null)
        {
            return checkResult;
        }

        var perm = await CanUseHotWallet();
        if ((!perm.CanCreateHotWallet && request.SavePrivateKeys) ||
            (!perm.CanRPCImport && request.ImportKeysToRPC) ||
            (!perm.CanCreateColdWallet && !request.SavePrivateKeys))
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
            SupportTaproot = network.NBitcoinNetwork.Consensus.SupportTaproot,
            SupportSegwit = network.NBitcoinNetwork.Consensus.SupportSegwit
        };
        vm.SetPermission(perm);
        if (isImport && string.IsNullOrEmpty(request.ExistingMnemonic))
        {
            ModelState.AddModelError(nameof(request.ExistingMnemonic), StringLocalizer["Please provide your existing seed"]);
            return StoreView(vm.ViewName, vm);
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
            return StoreView(vm.ViewName, vm);
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
        vm.Config = _dataProtector.ProtectString(JToken.FromObject(derivationSchemeSettings, handler.Serializer).ToString());

        var result = await UpdateWallet(vm);

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
    public async Task<ActionResult> GenerateWalletConfirm(string storeId, string cryptoCode)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(storeId, cryptoCode))
            return Forbid();
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
    public async Task<IActionResult> WalletSettings(string storeId, string cryptoCode)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(storeId, cryptoCode))
            return Forbid();
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
            NBXSeedAvailable = derivation.IsHotWallet &&
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
            CanUseRPCImport = perm.CanRPCImport,
            StoreName = store.StoreName,
            CanSetupMultiSig = (derivation.AccountKeySettings ?? []).Length > 1,
            IsMultiSigOnServer = derivation.IsMultiSigOnServer,
            DefaultIncludeNonWitnessUtxo = derivation.DefaultIncludeNonWitnessUtxo
        };

        ViewData["ReplaceDescription"] = WalletReplaceWarning(derivation.IsHotWallet);
        ViewData["RemoveDescription"] = WalletRemoveWarning(derivation.IsHotWallet, network.CryptoCode);

        return StoreView("WalletSettings", vm);
    }
        [HttpPost("{storeId}/onchain/{cryptoCode}/settings/wallet")]
    public async Task<IActionResult> UpdateWalletSettings(WalletSettingsViewModel vm)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(vm.StoreId, vm.CryptoCode))
            return Forbid();
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
        public async Task<IActionResult> WalletSeed(string storeId, string cryptoCode, CancellationToken cancellationToken = default)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(storeId, cryptoCode))
            return Forbid();
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
        public async Task<ActionResult> ReplaceWallet(string storeId, string cryptoCode)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(storeId, cryptoCode))
            return Forbid();
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
        public async Task<IActionResult> ConfirmReplaceWallet(string storeId, string cryptoCode)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(storeId, cryptoCode))
            return Forbid();
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
        public async Task<ActionResult> DeleteWallet(string storeId, string cryptoCode)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(storeId, cryptoCode))
            return Forbid();
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
        public async Task<IActionResult> ConfirmDeleteWallet(string storeId, string cryptoCode)
    {
        if (!await AuthorizeOnchainWalletSettingsAsync(storeId, cryptoCode))
            return Forbid();
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

        return RedirectToAction(nameof(UIStoresController.GeneralSettings), "UIStores", new { storeId });
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
        return StoreView("ImportWallet/ConfirmAddresses", vm);
    }

    private ViewResult StoreView(string viewName, object model = null)
    {
        var path = $"~/Views/UIStores/{viewName}.cshtml";
        return model is null ? View(path) : View(path, model);
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

    private async Task PopulateMultisigStoreUsers(WalletSetupViewModel vm)
    {
        var users = await _storeRepo.GetStoreUsers(vm.StoreId);
        var selected = vm.MultisigParticipantUserIds ?? Array.Empty<string>();
        vm.MultisigStoreUsers = users.Select(user => new MultisigStoreUserItem
        {
            UserId = user.Id,
            Email = user.Email,
            Name = user.UserBlob?.Name,
            Selected = selected.Contains(user.Id, StringComparer.Ordinal)
        }).ToList();
    }

    private void PopulateMultisigInviteLinks(WalletSetupViewModel vm)
    {
        vm.MultisigInviteLinks = new Dictionary<string, string>(StringComparer.Ordinal);
        if (vm.MultisigPendingSetup?.Participants is null)
            return;

        foreach (var participant in vm.MultisigPendingSetup.Participants)
        {
            var token = CreateMultisigInviteToken(vm.StoreId, vm.CryptoCode, vm.MultisigPendingSetup.RequestId, participant.UserId);
            var link = Url.Action(
                nameof(UIMultisigInviteController.SubmitMultisigSigner),
                "UIMultisigInvite",
                new { storeId = vm.StoreId, cryptoCode = vm.CryptoCode, token },
                Request.Scheme);
            if (!string.IsNullOrEmpty(link))
                vm.MultisigInviteLinks[participant.UserId] = link;
        }
    }

    private async Task<IActionResult> CreateMultisigRequest(WalletSetupViewModel vm)
    {
        var selectedIds = (vm.MultisigParticipantUserIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var totalSigners = vm.MultisigTotalSigners ?? 0;
        if (totalSigners <= 0 || totalSigners > 15)
        {
            ModelState.AddModelError(nameof(vm.MultisigTotalSigners), "Total signers must be between 1 and 15.");
            return StoreView(vm.ViewName, vm);
        }

        var usersById = vm.MultisigStoreUsers.ToDictionary(u => u.UserId, u => u, StringComparer.Ordinal);
        if (!selectedIds.All(usersById.ContainsKey))
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), "One or more selected users are invalid.");
            return StoreView(vm.ViewName, vm);
        }

        var requesterUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var requesterStoreUser = vm.MultisigStoreUsers.FirstOrDefault(u => string.Equals(u.UserId, requesterUserId, StringComparison.Ordinal));
        var requesterEmail = requesterStoreUser?.Email ?? User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var requesterName = requesterStoreUser?.Name;

        var pending = await GetLatestPendingMultisigSetup(vm.StoreId, vm.CryptoCode);
        var isNewRequest = pending is null;
        if (isNewRequest)
        {
            pending = new PendingMultisigSetupData
            {
                RequestId = Guid.NewGuid().ToString("N"),
                CryptoCode = vm.CryptoCode,
                RequestedByUserId = requesterUserId,
                RequestedByEmail = requesterEmail,
                RequestedByName = requesterName,
                ScriptType = vm.MultisigScriptType ?? "p2wsh",
                TotalSigners = totalSigners,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
            };
        }
        else if (pending.TotalSigners != totalSigners)
        {
            ModelState.AddModelError(nameof(vm.MultisigTotalSigners), $"This request is configured for {pending.TotalSigners} signers. Reset request to change N.");
            vm.MultisigPendingSetup = pending;
            vm.MultisigRequestId ??= pending.RequestId;
            return StoreView(vm.ViewName, vm);
        }

        if (pending.Participants is null)
            pending.Participants = new List<PendingMultisigSetupParticipantData>();

        var existingIds = pending.Participants.Select(p => p.UserId).ToHashSet(StringComparer.Ordinal);
        var newIds = selectedIds.Where(id => !existingIds.Contains(id)).ToArray();
        var resendIds = selectedIds
            .Where(existingIds.Contains)
            .Where(id => pending.Participants.Any(p =>
                string.Equals(p.UserId, id, StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(p.AccountKey)))
            .ToArray();
        var availableSlots = pending.TotalSigners - pending.Participants.Count;
        if (availableSlots <= 0 && newIds.Length > 0)
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), $"This request already has {pending.TotalSigners} signers. Replace a pending signer or reset request.");
            vm.MultisigPendingSetup = pending;
            vm.MultisigRequestId ??= pending.RequestId;
            return StoreView(vm.ViewName, vm);
        }
        if (newIds.Length > availableSlots)
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), $"Only {availableSlots} signer slot(s) left for this request.");
            vm.MultisigPendingSetup = pending;
            vm.MultisigRequestId ??= pending.RequestId;
            return StoreView(vm.ViewName, vm);
        }

        // Keep request persistent: merge newly selected users into the same active request.
        foreach (var selectedId in newIds)
        {
            var selectedUser = usersById[selectedId];
            pending.Participants.Add(new PendingMultisigSetupParticipantData
            {
                UserId = selectedUser.UserId,
                Email = selectedUser.Email,
                Name = selectedUser.Name
            });
        }

        if (pending.Participants.Count == 0)
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), "Select at least one signer.");
            return StoreView(vm.ViewName, vm);
        }

        var required = vm.MultisigRequiredSigners ?? pending.RequiredSigners;
        if (required <= 0 || required > pending.TotalSigners)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequiredSigners), "Required signatures must be between 1 and total signers (N).");
            return StoreView(vm.ViewName, vm);
        }

        pending.RequiredSigners = required;
        pending.ExpiresAt = DateTimeOffset.UtcNow.AddDays(7);

        await _storeRepo.UpdateSetting(vm.StoreId, GetPendingMultisigSettingName(vm.CryptoCode), pending);
        var emailTargetIds = newIds.Concat(resendIds).Distinct(StringComparer.Ordinal).ToArray();
        await SendMultisigRequestEmails(vm.StoreId, vm.CryptoCode, pending, emailTargetIds);
        TempData[WellKnownTempData.SuccessMessage] = isNewRequest
            ? "Multisig signer requests were created."
            : "Multisig signer requests were updated.";
        // PRG: avoid recreating a new request id when user refreshes after POST.
        return RedirectToAction(nameof(ImportWallet), new
        {
            storeId = vm.StoreId,
            cryptoCode = vm.CryptoCode,
            method = "multisig",
            multisigRequestId = pending.RequestId
        });
    }

    private async Task<IActionResult> RemoveMultisigSigner(WalletSetupViewModel vm)
    {
        var pending = await GetPendingMultisigSetup(vm.StoreId, vm.CryptoCode, vm.MultisigRequestId)
                      ?? await GetLatestPendingMultisigSetup(vm.StoreId, vm.CryptoCode);
        if (pending is null)
        {
            ModelState.AddModelError(nameof(vm.MultisigRequestId), "No active multisig request was found.");
            return StoreView(vm.ViewName, vm);
        }

        if (string.IsNullOrWhiteSpace(vm.MultisigRemoveUserId))
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), "Select a signer to remove.");
            vm.MultisigPendingSetup = pending;
            vm.MultisigRequestId = pending.RequestId;
            return StoreView(vm.ViewName, vm);
        }

        var removed = pending.Participants.RemoveAll(p => string.Equals(p.UserId, vm.MultisigRemoveUserId, StringComparison.Ordinal));
        if (removed == 0)
        {
            ModelState.AddModelError(nameof(vm.MultisigParticipantUserIds), "Selected signer is not part of this request.");
            vm.MultisigPendingSetup = pending;
            vm.MultisigRequestId = pending.RequestId;
            return StoreView(vm.ViewName, vm);
        }

        pending.ExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        await _storeRepo.UpdateSetting(vm.StoreId, GetPendingMultisigSettingName(vm.CryptoCode), pending);
        TempData[WellKnownTempData.SuccessMessage] = "Signer removed from request.";
        return RedirectToAction(nameof(ImportWallet), new
        {
            storeId = vm.StoreId,
            cryptoCode = vm.CryptoCode,
            method = "multisig",
            multisigRequestId = pending.RequestId
        });
    }

    private async Task<PendingMultisigSetupData> GetPendingMultisigSetup(string storeId, string cryptoCode, string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return null;
        var pending = await _storeRepo.GetSettingAsync<PendingMultisigSetupData>(storeId, GetPendingMultisigSettingName(cryptoCode));
        if (pending is null || !string.Equals(pending.RequestId, requestId, StringComparison.Ordinal))
            return null;
        if (pending.ExpiresAt < DateTimeOffset.UtcNow || pending.Finalized)
            return null;
        return pending;
    }

    private async Task<PendingMultisigSetupData> GetLatestPendingMultisigSetup(string storeId, string cryptoCode)
    {
        var pending = await _storeRepo.GetSettingAsync<PendingMultisigSetupData>(storeId, GetPendingMultisigSettingName(cryptoCode));
        if (pending is null)
            return null;
        if (pending.ExpiresAt < DateTimeOffset.UtcNow || pending.Finalized)
            return null;
        return pending;
    }

    private async Task SendMultisigRequestEmails(string storeId, string cryptoCode, PendingMultisigSetupData pending, IEnumerable<string> participantIds = null)
    {
        if (!await _emailSenderFactory.IsComplete(storeId))
            return;

        var sender = await _emailSenderFactory.GetEmailSender(storeId);
        var allowedIds = participantIds?.ToHashSet(StringComparer.Ordinal);
        foreach (var participant in pending.Participants.Where(p => string.IsNullOrWhiteSpace(p.AccountKey)))
        {
            if (allowedIds is not null && !allowedIds.Contains(participant.UserId))
                continue;
            var token = CreateMultisigInviteToken(storeId, cryptoCode, pending.RequestId, participant.UserId);
            var link = Url.Action(
                nameof(UIMultisigInviteController.SubmitMultisigSigner),
                "UIMultisigInvite",
                new { storeId, cryptoCode, token },
                Request.Scheme);
            if (string.IsNullOrEmpty(link))
                continue;

            sender.SendEmail(
                MailboxAddress.Parse(participant.Email),
                $"Multisig signer request for {cryptoCode}",
                $"A multisig wallet setup requires your account key.<br/>Open this link and submit your signer key:<br/><a href=\"{link}\">{link}</a>");
        }
    }

    private async Task SendMultisigWalletCreatedEmails(string storeId, string cryptoCode, PendingMultisigSetupData pending)
    {
        if (!await _emailSenderFactory.IsComplete(storeId))
            return;

        var sender = await _emailSenderFactory.GetEmailSender(storeId);
        var walletLink = Url.Action(
            nameof(WalletSettings),
            "UIStoreOnChainWallets",
            new { storeId, cryptoCode },
            Request.Scheme);
        if (string.IsNullOrEmpty(walletLink))
            return;

        foreach (var participant in pending.Participants.Where(p => !string.IsNullOrWhiteSpace(p.AccountKey)))
        {
            try
            {
                sender.SendEmail(
                    MailboxAddress.Parse(participant.Email),
                    $"Multisig wallet created for {cryptoCode}",
                    $"The multisig wallet setup is complete.<br/>Open wallet: <a href=\"{walletLink}\">{walletLink}</a>");
            }
            catch
            {
                // Do not fail wallet setup if notification delivery fails.
            }
        }
    }

    private string CreateMultisigInviteToken(string storeId, string cryptoCode, string requestId, string userId)
    {
        var payload = $"{storeId}|{cryptoCode}|{requestId}|{userId}|{DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()}";
        var protectedPayload = _multisigInviteProtector.Protect(payload);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedPayload));
    }

    internal static string GetPendingMultisigSettingName(string cryptoCode)
    {
        return $"{PendingMultisigSettingPrefix}-{cryptoCode?.ToUpperInvariant()}";
    }

    private static bool TryBuildMultisigDerivationScheme(WalletSetupViewModel vm, out string derivationScheme, out string validationError)
    {
        derivationScheme = null;
        validationError = null;

        if (!string.IsNullOrWhiteSpace(vm.MultisigManualDerivationScheme))
        {
            derivationScheme = vm.MultisigManualDerivationScheme.Trim();
            return true;
        }

        var requiredSigners = vm.MultisigRequiredSigners ?? 0;
        var totalSigners = vm.MultisigTotalSigners ?? 0;
        if (requiredSigners <= 0 || totalSigners <= 0 || requiredSigners > totalSigners)
        {
            validationError = "Invalid M-of-N configuration.";
            return false;
        }

        if (totalSigners > 15)
        {
            validationError = "Too many signers. Use 15 or fewer keys.";
            return false;
        }

        var rawSignerKeys = vm.MultisigSigners ?? Array.Empty<string>();
        var rawFingerprints = vm.MultisigSignerFingerprints ?? Array.Empty<string>();
        var rawKeyPaths = vm.MultisigSignerKeyPaths ?? Array.Empty<string>();

        var signerKeys = rawSignerKeys.Select(k => k?.Trim()).ToArray();
        var signerFingerprints = rawFingerprints.Select(k => k?.Trim()).ToArray();
        var signerKeyPaths = rawKeyPaths.Select(k => k?.Trim()).ToArray();

        signerKeys = signerKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();

        if (signerKeys.Length != totalSigners)
        {
            validationError = "Please provide all signer keys.";
            return false;
        }

        if (signerKeys.Distinct(StringComparer.Ordinal).Count() != signerKeys.Length)
        {
            validationError = "Signer keys must be unique.";
            return false;
        }

        var suffix = vm.MultisigScriptType?.ToLowerInvariant() switch
        {
            "p2wsh" => string.Empty,
            "p2sh-p2wsh" => "-[p2sh]",
            "p2sh" => "-[legacy]",
            _ => null
        };
        if (suffix is null)
        {
            validationError = "Invalid multisig script type.";
            return false;
        }

        bool hasPartialOriginInfo = false;
        for (var i = 0; i < totalSigners; i++)
        {
            var key = (rawSignerKeys.ElementAtOrDefault(i) ?? string.Empty).Trim();
            var fp = (rawFingerprints.ElementAtOrDefault(i) ?? string.Empty).Trim();
            var path = (rawKeyPaths.ElementAtOrDefault(i) ?? string.Empty).Trim();
            var hasOrigin = !string.IsNullOrWhiteSpace(fp) || !string.IsNullOrWhiteSpace(path);
            if (hasOrigin && (string.IsNullOrWhiteSpace(fp) || string.IsNullOrWhiteSpace(path)))
            {
                hasPartialOriginInfo = true;
            }

            if (!hasOrigin)
            {
                continue;
            }

            if (!Regex.IsMatch(fp, "^[0-9a-fA-F]{8}$"))
            {
                validationError = $"Signer {i + 1}: invalid fingerprint.";
                return false;
            }

            var normalizedPath = path
                .Replace("’", "'")
                .Replace("`", "'")
                .Replace("′", "'")
                .Replace(" ", string.Empty);
            normalizedPath = Regex.Replace(normalizedPath, @"([0-9]+)[hH]", "$1'");
            normalizedPath = normalizedPath.StartsWith("m/", StringComparison.OrdinalIgnoreCase) ? normalizedPath[2..] : normalizedPath;
            if (!KeyPath.TryParse(normalizedPath, out var parsedPath))
            {
                validationError = $"Signer {i + 1}: invalid account key path.";
                return false;
            }
        }

        if (hasPartialOriginInfo)
        {
            validationError = "For each signer, provide both fingerprint and account key path, or leave both empty.";
            return false;
        }

        derivationScheme = $"{requiredSigners}-of-{string.Join("-", signerKeys)}{suffix}";
        return true;
    }

    private static void ApplyMultisigSignerOrigins(WalletSetupViewModel vm, DerivationSchemeSettings strategy)
    {
        if (strategy.AccountKeySettings is null || strategy.AccountKeySettings.Length == 0)
            return;

        for (var i = 0; i < strategy.AccountKeySettings.Length; i++)
        {
            var fp = (vm.MultisigSignerFingerprints?.ElementAtOrDefault(i) ?? string.Empty).Trim();
            var path = (vm.MultisigSignerKeyPaths?.ElementAtOrDefault(i) ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fp) || string.IsNullOrWhiteSpace(path))
                continue;

            var normalizedPath = path
                .Replace("’", "'")
                .Replace("`", "'")
                .Replace("′", "'")
                .Replace(" ", string.Empty);
            normalizedPath = Regex.Replace(normalizedPath, @"([0-9]+)[hH]", "$1'");
            normalizedPath = normalizedPath.StartsWith("m/", StringComparison.OrdinalIgnoreCase) ? normalizedPath[2..] : normalizedPath;

            if (!KeyPath.TryParse(normalizedPath, out var parsedPath))
                continue;
            if (!Regex.IsMatch(fp, "^[0-9a-fA-F]{8}$"))
                continue;

            strategy.AccountKeySettings[i].AccountKeyPath = parsedPath;
            strategy.AccountKeySettings[i].RootFingerprint = new HDFingerprint(Encoders.Hex.DecodeData(fp));
        }
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

    private async Task<bool> AuthorizeOnchainWalletSettingsAsync(string storeId, string cryptoCode)
    {
        if ((await _authorizationService.AuthorizeAsync(User, storeId, Policies.CanManageWallets)).Succeeded)
        {
            if (HttpContext.GetStoreData() is null)
            {
                var store = await _storeRepo.FindStore(storeId);
                if (store != null)
                {
                    HttpContext.SetStoreData(store);
                }
            }
            return true;
        }
        var walletTypePolicy = cryptoCode.Equals("BTC", StringComparison.OrdinalIgnoreCase)
            ? Policies.CanModifyBitcoinOnchain
            : Policies.CanModifyOtherWallets;
        foreach (var policy in new[] { walletTypePolicy, Policies.CanManageWalletSettings })
        {
            if (!(await _authorizationService.AuthorizeAsync(User, storeId, policy)).Succeeded)
                return false;
        }
        if (HttpContext.GetStoreData() is null)
        {
            var store = await _storeRepo.FindStore(storeId);
            if (store != null)
            {
                HttpContext.SetStoreData(store);
            }
        }
        return true;
    }

    internal static DerivationSchemeSettings ParseDerivationStrategy(string derivationScheme, BTCPayNetwork network)
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
