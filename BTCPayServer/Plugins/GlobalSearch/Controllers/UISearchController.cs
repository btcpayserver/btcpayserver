using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.GlobalSearch
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Area(GlobalSearchPlugin.Area)]
    public class UISearchController(
        StoreRepository storeRepository,
        SearchResultItemProviders searchResultItemProviders)
        : Controller
    {
        [HttpGet("~/search/global")]
        [DisableCors]
        public async Task<IActionResult> Global(string q = null, string storeId = null, int take = 25)
        {
            var store = storeId is null ? null : await storeRepository.FindStore(storeId, User);
            var vm = await searchResultItemProviders.GetViewModel(this.User, store, this.Url, q);
            vm.Items = vm.Items.Take(take).ToList();
            return Json(vm.Items);
        }
    }
}
