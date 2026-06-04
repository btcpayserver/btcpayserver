#nullable enable

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Plugins.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NBitcoin;
using Npgsql;

namespace BTCPayServer.Plugins.Multisig.Controllers;

[Route("multisig-setups")]
[Authorize(Policy = WalletPolicies.CanViewWallet, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Area(MultisigPlugin.Area)]
public class UIMultisigSetupController(
    MultisigService multisigService,
    IStringLocalizer stringLocalizer,
    BTCPayNetworkProvider networkProvider,
    MultisigNotificationService multisigNotificationService,
    IAuthorizationService authorizationService) : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    [HttpGet("{multisigSetupId}")]
    public async Task<IActionResult> SetupMultisigStatus(string multisigSetupId)
    {
        var setup = await multisigService.GetPendingMultisigSetupContext(this.HttpContext.GetStoreData().Id, multisigSetupId);
        var store = HttpContext.GetStoreDataOrNull();
        if (setup is null || store is null)
            return NotFound();

        if (!setup.ReplacesExistingWallet && multisigService.HasOnChainWallet(store, setup.CryptoCode))
            return NotFound();

        var setupAccess = await authorizationService.GetSetupAccess(setup.StoreId, User, setup);
        if (!setupAccess.CanViewStatus)
            return Forbid();

        var model = multisigService.CreateInProgressViewModel(setup.StoreId, User.GetId(), setup, setupAccess.CanManageWalletSettings);
        return View(model);
    }

    private const string HardwareInputMethod = "hardware";
    private const string ManualInputMethod = "manual";

    private static bool IsSupportedCryptoCode(string? cryptoCode) =>
        string.Equals(cryptoCode, "BTC", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeInputMethod(string? method) =>
        string.Equals(method, HardwareInputMethod, StringComparison.OrdinalIgnoreCase) ? HardwareInputMethod :
        string.Equals(method, ManualInputMethod, StringComparison.OrdinalIgnoreCase) ? ManualInputMethod :
        null;

    private enum SignerKeyLoadStatus
    {
        Ok,
        Invalid,
        Forbidden
    }

    private sealed class SignerKeyLoadResult
    {
        public SignerKeyLoadStatus Status { get; init; }
        public string? UserId { get; init; }
        public MultisigSignerKeyViewModel? ViewModel { get; init; }
    }

    [HttpGet("{multisigSetupId}/signer-key")]
    public async Task<IActionResult> SetupMultisigSubmitKey(string multisigSetupId, string? method = null)
    {
        var result = await LoadSignerKeyViewModel(this.HttpContext.GetStoreData().Id, multisigSetupId);
        if (result.Status is SignerKeyLoadStatus.Forbidden)
            return Forbid();
        if (result.Status is not SignerKeyLoadStatus.Ok)
            return NotFound();
        var vm = result.ViewModel!;
        if (!string.IsNullOrWhiteSpace(vm.DisplayAccountKey))
        {
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Your signer key is submitted."].Value;
            return RedirectToAction(nameof(UIMultisigSetupController.SetupMultisigStatus), "UIMultisigSetup",
                new { area = MultisigPlugin.Area, multisigSetupId = vm.RequestId });
        }

        vm.InputMethod = NormalizeInputMethod(method);
        return View(vm);
    }

    [HttpPost("{multisigSetupId}/signer-key")]
    public async Task<IActionResult> SetupMultisigSubmitKey(string multisigSetupId, MultisigSignerKeyViewModel vm)
    {
        var result = await LoadSignerKeyViewModel(this.HttpContext.GetStoreData().Id, multisigSetupId);
        if (result.Status is SignerKeyLoadStatus.Forbidden)
            return Forbid();
        if (result.Status is not SignerKeyLoadStatus.Ok)
            return NotFound();
        var current = result.ViewModel!;
        var currentUserId = result.UserId!;

        var network = networkProvider.GetNetwork(current.CryptoCode) as BTCPayNetwork;
        if (network is null)
            return NotFound();

        current.DisplayAccountKey = vm.DisplayAccountKey?.Trim();
        current.AccountKeyPath = vm.AccountKeyPath?.Trim();
        current.InputMethod = NormalizeInputMethod(vm.InputMethod) ?? ManualInputMethod;

        BitcoinExtPubKey? accountKey = null;
        if (string.IsNullOrWhiteSpace(current.DisplayAccountKey))
            ModelState.AddModelError(nameof(vm.DisplayAccountKey), StringLocalizer["Please provide your account key."].Value);
        else
        {
            try
            {
                accountKey = new BitcoinExtPubKey(current.DisplayAccountKey, network.NBitcoinNetwork);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, StringLocalizer["Invalid account key format."].Value);
            }
        }

        if (!RootedKeyPath.TryParse(current.AccountKeyPath ?? "", out var accountKeyPath))
            ModelState.AddModelError(nameof(vm.AccountKeyPath), StringLocalizer["Invalid account key path."].Value);
        else
            current.AccountKeyPath = accountKeyPath.ToString();

        if (!ModelState.IsValid)
            return View(current);

        var storeId = this.HttpContext.GetStoreData().Id;
        var normalizedAccountKey = accountKey!.ToString();
        var pending = await multisigService.GetPendingMultisigSetupContext(storeId, current.RequestId);
        if (pending is null)
            return NotFound();

        var participant = pending.Participants.Find(p => string.Equals(p.UserId, currentUserId, StringComparison.Ordinal));
        if (participant is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(participant.AccountKey))
        {
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Your signer key is submitted."].Value;
            return RedirectToAction(nameof(UIMultisigSetupController.SetupMultisigStatus), "UIMultisigSetup",
                new { area = MultisigPlugin.Area, multisigSetupId = current.RequestId });
        }

        participant.AccountKey = normalizedAccountKey;
        participant.AccountKeyPath = accountKeyPath;

        try
        {
            await multisigService.UpdateParticipant(storeId, pending.RequestId, currentUserId, participant);
        }
        catch (PostgresException e) when (e is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UQ_multisig_setups_participants_multisig_setup_id_account_key" })
        {
            ModelState.AddModelError(string.Empty, StringLocalizer["This signer key is already used in this multisig request."].Value);
            return View(current);
        }

        multisigNotificationService.PublishSignerKeySubmittedEvent(pending, participant);
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Signer key submitted successfully."].Value;
        return RedirectToAction(nameof(UIMultisigSetupController.SetupMultisigStatus), "UIMultisigSetup",
            new { area = MultisigPlugin.Area, multisigSetupId = current.RequestId });
    }

    private async Task<SignerKeyLoadResult> LoadSignerKeyViewModel(string storeId, string multisigSetupId)
    {
        var currentUserId = User.GetId();
        var pending = await multisigService.GetPendingMultisigSetupContext(storeId, multisigSetupId);
        if (pending is null || !IsSupportedCryptoCode(pending.CryptoCode))
            return new SignerKeyLoadResult { Status = SignerKeyLoadStatus.Invalid };

        var access = await authorizationService.GetSetupAccess(storeId, User, pending);
        if (!access.CanSignWalletTransactions || !access.IsParticipant)
            return new SignerKeyLoadResult { Status = SignerKeyLoadStatus.Forbidden };

        var participant = pending.Participants.Find(p => string.Equals(p.UserId, currentUserId, StringComparison.Ordinal));
        if (participant is null)
            return new SignerKeyLoadResult { Status = SignerKeyLoadStatus.Invalid };

        return new SignerKeyLoadResult
        {
            Status = SignerKeyLoadStatus.Ok,
            UserId = participant.UserId,
            ViewModel = new MultisigSignerKeyViewModel
            {
                CryptoCode = pending.CryptoCode,
                RequestId = pending.RequestId,
                RequiredSigners = pending.RequiredSigners,
                TotalSigners = pending.TotalSigners,
                ScriptType = pending.ScriptType,
                DisplayAccountKey = participant.AccountKey,
                AccountKeyPath = participant.AccountKeyPath?.ToString()
            }
        };
    }
}
