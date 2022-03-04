using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.StoreWalletBalance;

public class StoreWalletBalance : ViewComponent
{
    private readonly StoreRepository _storeRepo;
    private readonly UserManager<ApplicationUser> _userManager;

    public StoreWalletBalance(
        StoreRepository storeRepo,
        UserManager<ApplicationUser> userManager)
    {
        _storeRepo = storeRepo;
        _userManager = userManager;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var userId = _userManager.GetUserId(UserClaimsPrincipal);
        var vm = new StoreWalletBalanceViewModel
        {
            Store = store
        };

        return View(vm);
    }
}
