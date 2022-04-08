using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.AppSales;

public class AppSales : ViewComponent
{
    private readonly AppService _appService;
    private readonly StoreRepository _storeRepo;

    public AppSales(AppService appService, StoreRepository storeRepo)
    {
        _appService = appService;
        _storeRepo = storeRepo;
    }

    public async Task<IViewComponentResult> InvokeAsync(AppData app)
    {
        var stats = await _appService.GetSalesStats(app);
        var vm = new AppSalesViewModel
        {
            App = app,
            SalesCount = stats.SalesCount,
            Series = stats.Series
        };

        return View(vm);
    }
}
