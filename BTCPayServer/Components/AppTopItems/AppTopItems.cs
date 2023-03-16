using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Components.AppSales;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.AppTopItems;

public class AppTopItems : ViewComponent
{
    private readonly AppService _appService;

    public AppTopItems(AppService appService)
    {
        _appService = appService;
    }

    public async Task<IViewComponentResult> InvokeAsync(string appId, string appType = null)
    {
        var vm = new AppTopItemsViewModel
        {
            Id = appId,
            AppType = appType,
            DataUrl = Url.Action("AppTopItems", "UIApps", new { appId }),
            InitialRendering = HttpContext.GetAppData()?.Id != appId
        };
        if (vm.InitialRendering)
            return View(vm);

        var app = HttpContext.GetAppData();
        var entries = await _appService.GetItemStats(app);
        vm.SalesCount = entries.Select(e => e.SalesCount).ToList();
        vm.Entries = entries.ToList();
        vm.AppType = app.AppType;
        vm.AppUrl = await _appService.ConfigureLink(app, app.AppType);

        return View(vm);
    }
}
