using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Components.AppTopItems;

public class AppTopItems(AppService appService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string appId, string appType)
    {
        var type = appService.GetAppType(appType);
        if (type is not (IHasItemStatsAppType and AppBaseType appBaseType))
            return new HtmlContentViewComponentResult(new StringHtmlContent(string.Empty));

        var vm = new AppTopItemsViewModel
        {
            Id = appId,
            AppType = appType,
            DataUrl = Url.Action("AppTopItems", "UIApps", new { appId }),
            InitialRendering = HttpContext.GetAppDataOrNull()?.Id != appId
        };
        if (vm.InitialRendering)
            return View(vm);

        var app = HttpContext.GetAppDataOrNull();
        var entries = await appService.GetItemStats(app);
        vm.SalesCount = entries.Select(e => e.SalesCount).ToList();
        vm.Entries = entries.Take(5).ToList();
        vm.AppType = app.AppType;
        vm.AppUrl = await appBaseType.ConfigureLink(app);
        vm.Name = app.Name;

        return View(vm);
    }
}
