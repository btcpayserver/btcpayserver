#nullable enable

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Plugins.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Multisig.Controllers;

[Route("multisig-setups")]
[Authorize(Policy = WalletPolicies.CanViewWallet, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Area(MultisigPlugin.Area)]
public class UIMultisigStatusController(
    MultisigService multisigService) : Controller
{
    [HttpGet("{multisigSetupId}")]
    public async Task<IActionResult> Status(string multisigSetupId)
    {
        var setupContext = await multisigService.GetPendingMultisigSetupContext(multisigSetupId);
        var store = HttpContext.GetStoreDataOrNull();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (setupContext is null || store is null || string.IsNullOrEmpty(userId))
            return NotFound();

        if (!string.Equals(store.Id, setupContext.StoreId, StringComparison.Ordinal))
            return NotFound();

        if (!setupContext.Pending.ReplacesExistingWallet && multisigService.HasOnChainWallet(store, setupContext.CryptoCode))
            return NotFound();

        var setupAccess = await multisigService.GetSetupAccess(setupContext.StoreId, User, userId, setupContext.Pending);
        if (!setupAccess.CanViewStatus)
            return Forbid();

        var model = multisigService.CreateInProgressViewModel(setupContext.StoreId, userId, setupContext.CryptoCode, setupContext.Pending, HttpContext, setupAccess.CanManageWalletSettings);
        return View("MultisigStatus", model);
    }
}
