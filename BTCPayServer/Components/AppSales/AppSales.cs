using System;
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

    public async Task<IViewComponentResult> InvokeAsync(AppSalesViewModel vm)
    {
        if (vm.App == null) throw new ArgumentNullException(nameof(vm.App));
        if (vm.InitialRendering) return View(vm);
        
        var stats = await _appService.GetSalesStats(vm.App);

        vm.SalesCount = stats.SalesCount;
        vm.Series = stats.Series;

        return View(vm);
    }
}
