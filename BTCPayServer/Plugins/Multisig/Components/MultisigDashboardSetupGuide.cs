using System.Threading.Tasks;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Plugins.Multisig.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Multisig.Components;

public class MultisigDashboardSetupGuide(MultisigService multisigService, IAuthorizationService authorizationService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(StoreDashboardViewModel model)
    {
        var httpContext = ViewContext.HttpContext;
        var store = httpContext.GetStoreData();
        var userId = httpContext.User.GetIdOrNull();
        if (string.IsNullOrEmpty(userId))
            return Content(string.Empty);

        var items = await multisigService.GetInProgressForStore(authorizationService, store, httpContext.User);
        if (items.Count == 0)
            return Content(string.Empty);

        return View("/Plugins/Multisig/Views/Shared/Components/MultisigDashboardSetupGuide/Default.cshtml", items);
    }
}
