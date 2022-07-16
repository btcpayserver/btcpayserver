using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Security;

public class AuthorizationFilterArguments
{
    public AuthorizationHandlerContext Context { get; }
    public PolicyRequirement Requirement { get; }
    public HttpContext HttpContext { get; }
        
    public AuthorizationFilterArguments(
        AuthorizationHandlerContext context,
        PolicyRequirement requirement,
        HttpContext httpContext)
    {
        Context = context;
        Requirement = requirement;
        HttpContext = httpContext;
    }
}
