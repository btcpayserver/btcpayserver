#nullable enable
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Client;
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

    /// <summary>
    /// The user's query. When null, the items returned by <see cref="ISearchResultItemProvider" /> get filtered browser's side.
    /// </summary>
    public string? UserQuery { get; set; }
    public async Task<bool> IsAuthorized(string policy)
    {
        var type = Permission.TryGetPolicyType(policy);
        if (type == PolicyType.Store)
        {
            if (Store is null)
                return false;
            var result = await AuthorizationService.AuthorizeAsync(User, Store, new PolicyRequirement(policy));
            return result.Succeeded;
        }
        else
        {
            var result = await AuthorizationService.AuthorizeAsync(User, null, new PolicyRequirement(policy));
            return result.Succeeded;
        }
    }
}

public interface ISearchResultItemProvider
{
    Task ProvideAsync(SearchResultItemProviderContext context);
}
