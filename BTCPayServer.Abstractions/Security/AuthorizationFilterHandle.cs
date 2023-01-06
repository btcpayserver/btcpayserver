using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Security;

public class AuthorizationFilterHandle
{
    public AuthorizationHandlerContext Context { get; }
    public PolicyRequirement Requirement { get; }
    public HttpContext HttpContext { get; }
    public bool Success { get; private set; }

    public AuthorizationFilterHandle(
        AuthorizationHandlerContext context,
        PolicyRequirement requirement,
        HttpContext httpContext)
    {
        Context = context;
        Requirement = requirement;
        HttpContext = httpContext;
    }

    public void MarkSuccessful()
    {
        Success = true;
    }
}
