using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactions : ViewComponent
{
    private readonly StoreRepository _storeRepo;
    private readonly UserManager<ApplicationUser> _userManager;

    public StoreRecentTransactions(
        StoreRepository storeRepo,
        UserManager<ApplicationUser> userManager)
    {
        _storeRepo = storeRepo;
        _userManager = userManager;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var userId = _userManager.GetUserId(UserClaimsPrincipal);
        var entries = System.Array.Empty<object>();
        var vm = new StoreRecentTransactionsViewModel
        {
            Store = store,
            WalletId = new WalletId(store.Id, "BTC"),
            Entries = entries
        };

        return View(vm);
    }
}
