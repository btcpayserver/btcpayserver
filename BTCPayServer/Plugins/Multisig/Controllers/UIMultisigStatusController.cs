#nullable enable

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Plugins.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Multisig.Controllers;

[Route("multisig-setups")]
[Authorize(Policy = WalletPolicies.CanViewWallet, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Area(MultisigPlugin.Area)]
public class UIMultisigStatusController(
    MultisigService multisigService,
    IAuthorizationService authorizationService) : Controller
{
    [HttpGet("{multisigSetupId}")]
    public async Task<IActionResult> Status(string multisigSetupId)
    {
        var setupContext = await multisigService.GetPendingMultisigSetupContext(multisigSetupId);
        var store = HttpContext.GetStoreDataOrNull();
        if (setupContext is null || store is null)
            return NotFound();

        if (!string.Equals(store.Id, setupContext.StoreId, StringComparison.Ordinal))
            return NotFound();

        if (!setupContext.Pending.ReplacesExistingWallet && multisigService.HasOnChainWallet(store, setupContext.Pending.CryptoCode))
            return NotFound();

        var setupAccess = await authorizationService.GetSetupAccess(setupContext.StoreId, User, setupContext.Pending);
        if (!setupAccess.CanViewStatus)
            return Forbid();

        var model = multisigService.CreateInProgressViewModel(setupContext.StoreId, User.GetId(), setupContext.Pending, setupAccess.CanManageWalletSettings);
        return View("MultisigStatus", model);
    }
}
