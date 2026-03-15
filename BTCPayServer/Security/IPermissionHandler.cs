#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Security;

public class PermissionAuthorizationContext(PolicyRequirement requirement, string? scope, string userId, HttpContext httpContext)
{
    public string UserId { get; set; } = userId;
    public HttpContext HttpContext { get; } = httpContext;
    public Permission Permission { get; } = Permission.Create(requirement.Policy, scope);
    public bool ExplicitScope { get; set; }
    public PolicyRequirement Requirement { get; } = requirement;
}
public interface IPermissionHandler
{
    Task HandleAsync(AuthorizationHandlerContext authContext, PermissionAuthorizationContext permContext);
}
