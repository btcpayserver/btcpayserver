using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.StoreSelector
{
    public class StoreSelector : ViewComponent
    {
        private readonly StoreRepository _storeRepo;
        private readonly UriResolver _uriResolver;
        private readonly UserManager<ApplicationUser> _userManager;

        public StoreSelector(
            StoreRepository storeRepo,
            UriResolver uriResolver,
            UserManager<ApplicationUser> userManager)
        {
            _storeRepo = storeRepo;
            _uriResolver = uriResolver;
            _userManager = userManager;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var userId = _userManager.GetUserId(UserClaimsPrincipal);
            var stores = await _storeRepo.GetStoresByUserId(userId);
            var currentStore = ViewContext.HttpContext.GetStoreData();
            var archivedCount = stores.Count(s => s.Archived);
            var options = stores
                .Where(store => !store.Archived)
                .Select(store => new StoreSelectorOption
                {
                    Text = store.StoreName,
                    Value = store.Id,
                    Selected = store.Id == currentStore?.Id
                })
                .OrderBy(s => s.Text)
                .ToList();

            var blob = currentStore?.GetStoreBlob();

            var vm = new StoreSelectorViewModel
            {
                Options = options,
                CurrentStoreId = currentStore?.Id,
                CurrentDisplayName = currentStore?.StoreName,
                CurrentStoreLogoUrl = await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), blob?.LogoUrl),
                ArchivedCount = archivedCount
            };

            return View(vm);
        }
    }
}
