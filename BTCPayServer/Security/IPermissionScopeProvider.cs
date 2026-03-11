#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Security;

public class ScopeProviderAuthorizationContext(string userId, PolicyRequirement requirement, HttpContext httpContext)
{
    public string UserId { get; } = userId;
    public PolicyRequirement Requirement { get; } = requirement;
    public HttpContext HttpContext { get; } = httpContext;
}

public interface IPermissionScopeProvider
{
    Task<string?> GetScope(AuthorizationHandlerContext authContext, ScopeProviderAuthorizationContext providerContext);
}
