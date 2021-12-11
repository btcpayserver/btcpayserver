using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NBitcoin.Secp256k1;

namespace BTCPayServer.Components.StoreSelector
{
    public class StoreSelector : ViewComponent
    {
        private const string RootName = "Global";
        private readonly StoreRepository _storeRepo;
        private readonly UserManager<ApplicationUser> _userManager;

        public StoreSelector(StoreRepository storeRepo, UserManager<ApplicationUser> userManager)
        {
            _storeRepo = storeRepo;
            _userManager = userManager;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var userId = _userManager.GetUserId(UserClaimsPrincipal);
            var stores = await _storeRepo.GetStoresByUserId(userId);
            var currentStore = ViewContext.HttpContext.GetStoreData();
            var options = stores
                .Select(store => new SelectListItem
                {
                    Text = store.StoreName,
                    Value = store.Id,
                    Selected = store.Id == currentStore?.Id
                })
                .ToList();
            
            var vm = new StoreSelectorViewModel
            {
                Options = options,
                CurrentStoreId = currentStore?.Id,
                CurrentDisplayName = currentStore?.StoreName ?? RootName
            };
            
            return View(vm);
        }
    }
}
