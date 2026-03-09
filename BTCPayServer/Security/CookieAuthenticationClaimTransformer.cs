using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Security.Greenfield;

namespace BTCPayServer.Security;

public class CookieAuthenticationClaimTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is { AuthenticationType : AuthenticationSchemes.Cookie } and ClaimsIdentity claimsIdentity
            && !claimsIdentity.HasClaim(c => c.Type == GreenfieldConstants.ClaimTypes.Permission))
        {
            claimsIdentity.AddClaim(new Claim(GreenfieldConstants.ClaimTypes.Permission,
                Permission.Create(Policies.Unrestricted).ToString()));
        }
        return Task.FromResult(principal);
    }
}
