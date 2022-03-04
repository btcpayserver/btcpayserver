using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.StoreNumbers;

public class StoreNumbers : ViewComponent
{
    private readonly StoreRepository _storeRepo;
    private readonly UserManager<ApplicationUser> _userManager;

    public StoreNumbers(
        StoreRepository storeRepo,
        UserManager<ApplicationUser> userManager)
    {
        _storeRepo = storeRepo;
        _userManager = userManager;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var userId = _userManager.GetUserId(UserClaimsPrincipal);
        var vm = new StoreNumbersViewModel
        {
            Store = store,
            WalletId = new WalletId(store.Id, "BTC"),
            PayoutsPending = 4,
            Transactions = 92,
            RefundsIssued = 2
        };

        return View(vm);
    }
}
