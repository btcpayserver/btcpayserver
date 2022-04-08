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
        var entries = await _appService.GetPerkStats(app);
        var vm = new AppTopItemsViewModel
        {
            App = app,
            Entries = entries
        };

        return View(vm);
    }
}
