using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Components.StoreSelector
{
    public class StoreSelector : ViewComponent
    {
        private readonly StoreRepository _storeRepo;
        private readonly UserManager<ApplicationUser> _userManager;

        public StoreSelector(StoreRepository storeRepo, UserManager<ApplicationUser> userManager)
        {
            _storeRepo = storeRepo;
            _userManager = userManager;
        }

        public async Task<IViewComponentResult> InvokeAsync(string currentOptionId = null)
        {
            var userId = _userManager.GetUserId(UserClaimsPrincipal);
            var stores = await _storeRepo.GetStoresByUserId(userId);
            var options = stores
                .Select(store => new SelectListItem
                {
                    Text = store.StoreName,
                    Value = store.Id,
                    Selected = store.Id == currentOptionId
                })
                .Prepend(new SelectListItem("Dashboard", null, currentOptionId == null))
                .ToList();
            var vm = new StoreSelectorViewModel
            {
                Options = options,
                CurrentStore = currentOptionId
            };
            
            return View(vm);
        }
    }
}
