using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.AppSales;

public class AppSales : ViewComponent
{
    private readonly StoreRepository _storeRepo;
    private readonly UserManager<ApplicationUser> _userManager;

    public AppSales(
        StoreRepository storeRepo,
        UserManager<ApplicationUser> userManager)
    {
        _storeRepo = storeRepo;
        _userManager = userManager;
    }

    public async Task<IViewComponentResult> InvokeAsync(AppData app)
    {
        var userId = _userManager.GetUserId(UserClaimsPrincipal);
        var entries = System.Array.Empty<object>();
        var vm = new AppSalesViewModel
        {
            App = app,
            Entries = entries
        };

        return View(vm);
    }
}
