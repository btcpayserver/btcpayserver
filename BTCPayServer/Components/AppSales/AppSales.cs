using System;
using System.Security.AccessControl;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.AppSales;

public enum AppSalesPeriod
{
    Week,
    Month
}

public class AppSales : ViewComponent
{
    private readonly AppService _appService;

    public AppSales(AppService appService)
    {
        _appService = appService;
    }

    public async Task<IViewComponentResult> InvokeAsync(string appId, string appType)
    {
        var vm = new AppSalesViewModel()
        {
            Id = appId,
            AppType = appType,
            Url = Url.Action("AppSales", "UIApps", new { appId = appId }),
            InitialRendering = HttpContext.GetAppData()?.Id != appId
        };
        if (vm.InitialRendering)
            return View(vm);
        var app = HttpContext.GetAppData();
        vm.AppType = app.AppType;
        var stats = await _appService.GetSalesStats(HttpContext.GetAppData());
        vm.SalesCount = stats.SalesCount;
        vm.Series = stats.Series;

        return View(vm);
    }
}
