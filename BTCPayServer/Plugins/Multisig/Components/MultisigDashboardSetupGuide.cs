using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Plugins.Multisig.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Multisig.Components;

public class MultisigDashboardSetupGuide(MultisigService multisigService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(StoreDashboardViewModel model)
    {
        var httpContext = ViewContext.HttpContext;
        var store = httpContext.GetStoreData();
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Content(string.Empty);

        var items = await multisigService.GetInProgressForStore(store, userId, httpContext);
        if (items.Count == 0)
            return Content(string.Empty);

        return View("/Plugins/Multisig/Views/Shared/Components/MultisigDashboardSetupGuide/Default.cshtml", items);
    }
}
