using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.StoreSelector
{
    public class StoreSelector(
        StoreRepository storeRepo,
        UriResolver uriResolver)
        : ViewComponent
    {
        public async Task<IViewComponentResult> InvokeAsync()
        {
            var userId = UserClaimsPrincipal.GetId();
            var stores = await storeRepo.GetStoresByUserId(userId);
            var currentStore = ViewContext.HttpContext.GetNavStoreData();
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
                CurrentStoreLogoUrl = await uriResolver.Resolve(Request.GetAbsoluteRootUri(), blob?.LogoUrl),
                ArchivedCount = archivedCount
            };

            return View(vm);
        }
    }
}
