using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.AppTopItems;

public class AppTopItems : ViewComponent
{
    private readonly AppService _appService;
    private readonly StoreRepository _storeRepo;

    public AppTopItems(AppService appService, StoreRepository storeRepo)
    {
        _appService = appService;
        _storeRepo = storeRepo;
    }

    public async Task<IViewComponentResult> InvokeAsync(AppData app)
    {
        var entries = Enum.Parse<AppType>(app.AppType) == AppType.Crowdfund
            ? await _appService.GetPerkStats(app)
            : await _appService.GetItemStats(app);
        var vm = new AppTopItemsViewModel
        {
            App = app,
            Entries = entries.ToList()
        };

        return View(vm);
    }
}
