using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Multisig.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Multisig.Components;

public class MultisigWalletsInProgress(StoreRepository storeRepository, MultisigService multisigService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var httpContext = ViewContext.HttpContext;
        var user = httpContext.User;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Content(string.Empty);

        var stores = await storeRepository.GetStoresByUserId(userId);
        var items = await multisigService.GetInProgressForUser(stores, user, userId, httpContext);
        if (items.Count == 0)
            return Content(string.Empty);

        return View("/Plugins/Multisig/Views/Shared/Components/MultisigWalletsInProgress/Default.cshtml", items);
    }
}
