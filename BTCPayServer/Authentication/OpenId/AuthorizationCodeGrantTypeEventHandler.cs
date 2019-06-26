using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Authentication.OpenId
{
    public class AuthorizationCodeGrantTypeEventHandler : OpenIdGrantHandlerCheckCanSignIn
    {
        public AuthorizationCodeGrantTypeEventHandler(SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions, UserManager<ApplicationUser> userManager) : base(signInManager,
            identityOptions, userManager)
        {
        }

        protected override bool IsValid(OpenIdConnectRequest request)
        {
            return request.IsAuthorizationCodeGrantType();
        }
    }
}