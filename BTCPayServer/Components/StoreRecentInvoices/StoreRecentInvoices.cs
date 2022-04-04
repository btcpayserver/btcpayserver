using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.StoreRecentInvoices;

public class StoreRecentInvoices : ViewComponent
{
    private readonly StoreRepository _storeRepo;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContextFactory _dbContextFactory;

    public StoreRecentInvoices(
        StoreRepository storeRepo,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContextFactory dbContextFactory)
    {
        _storeRepo = storeRepo;
        _userManager = userManager;
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var userId = _userManager.GetUserId(UserClaimsPrincipal);
        var entries = System.Array.Empty<object>();
        var vm = new StoreRecentInvoicesViewModel
        {
            Store = store,
            Entries = entries
        };

        return View(vm);
    }
}
