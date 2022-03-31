using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBXplorer;

namespace BTCPayServer.Components.StoreWalletBalance;

public class StoreWalletBalance : ViewComponent
{
    private const string CryptoCode = "BTC";
    
    private readonly StoreRepository _storeRepo;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ExplorerClientProvider _explorerClientProvider;

    public StoreWalletBalance(
        StoreRepository storeRepo,
        UserManager<ApplicationUser> userManager,
        ExplorerClientProvider explorerClientProvider)
    {
        _storeRepo = storeRepo;
        _userManager = userManager;
        _explorerClientProvider = explorerClientProvider;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var userId = _userManager.GetUserId(UserClaimsPrincipal);
        var explorerClient = _explorerClientProvider.GetExplorerClient(CryptoCode);
        
        // TODO: https://github.com/dgarage/NBXplorer/blob/ad0d3c79f57f20e724fd57dd692608c2476d6b1d/docs/Postgres-Schema.md#function-get_wallets_histogram
        
        var vm = new StoreWalletBalanceViewModel
        {
            Store = store,
            CryptoCode = CryptoCode
        };

        return View(vm);
    }
}
