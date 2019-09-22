using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Data;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Core;

namespace BTCPayServer.Authentication.OpenId
{
    public class RefreshTokenGrantTypeEventHandler : OpenIdGrantHandlerCheckCanSignIn
    {
        public RefreshTokenGrantTypeEventHandler(
            OpenIddictApplicationManager<BTCPayOpenIdClient> applicationManager,
            OpenIddictAuthorizationManager<BTCPayOpenIdAuthorization> authorizationManager,
            SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions, UserManager<ApplicationUser> userManager) : base(
            applicationManager, authorizationManager, signInManager,
            identityOptions, userManager)
        {
        }

        protected override bool IsValid(OpenIdConnectRequest request)
        {
            return request.IsRefreshTokenGrantType();
        }
    }
}
