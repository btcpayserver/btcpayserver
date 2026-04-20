using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Services.Stores;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BTCPayServer.Plugins.GlobalSearch;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Area(GlobalSearchPlugin.Area)]
public class UISearchController(
    StoreRepository storeRepository,
    SearchResultItemProviders searchResultItemProviders)
    : Controller
{
    [HttpGet("~/search/global")]
    [DisableCors]
    public async Task<IActionResult> Global(
        string q = null,
        string storeId = null,
        int? take = null,
        string hash = null,
        CancellationToken cancellationToken = default)
    {
        var store = storeId is null ? null : await storeRepository.FindStore(storeId, User);
        var vm = await searchResultItemProviders.GetViewModel(this.User, store, this.Url, q, cancellationToken: cancellationToken);
        if (take is not null)
            vm.Items = vm.Items.Take(take.Value).ToList();
        if (hash != null)
        {
            const int durationInSeconds = 60 * 60 * 24 * 365;
            HttpContext.Response.Headers[HeaderNames.CacheControl] = "public,max-age=" + durationInSeconds;
        }
        return Json(vm.Items);
    }
}
