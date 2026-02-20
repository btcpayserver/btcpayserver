#nullable enable
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.GlobalSearch.Views;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.GlobalSearch;

public class SearchResultItemProviderContext(ClaimsPrincipal user, string userId, IUrlHelper url, IAuthorizationService authorizationService)
{
    public StoreData? Store { get; set; }
    public ClaimsPrincipal User { get; } = user;
    public string UserId { get; } = userId;
    public List<ResultItemViewModel> ItemResults { get; set; } = new();
    public IUrlHelper Url { get; } = url;
    public IAuthorizationService AuthorizationService { get; } = authorizationService;
    public async Task<bool> IsAuthorized(string policy)
    {
        if (Store is null)
            return false;
        var result = await AuthorizationService.AuthorizeAsync(User, Store, new PolicyRequirement(policy));
        return result.Succeeded;
    }
}

public interface ISearchResultItemProvider
{
    Task ProvideAsync(SearchResultItemProviderContext context);
}
