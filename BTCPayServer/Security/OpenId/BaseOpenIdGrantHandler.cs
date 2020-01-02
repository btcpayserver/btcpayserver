using System.Threading.Tasks;
using OpenIdConnectRequest = OpenIddict.Abstractions.OpenIddictRequest;
using BTCPayServer.Data;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Core;
using OpenIddict.Server;
using System.Security.Claims;

namespace BTCPayServer.Security.OpenId
{
    public abstract class BaseOpenIdGrantHandler<T> : 
        IOpenIddictServerHandler<T>
        where T : OpenIddictServerEvents.BaseContext
    {
        private readonly OpenIddictApplicationManager<BTCPayOpenIdClient> _applicationManager;
        private readonly OpenIddictAuthorizationManager<BTCPayOpenIdAuthorization> _authorizationManager;
        protected readonly SignInManager<ApplicationUser> _signInManager;
        protected readonly IOptions<IdentityOptions> _identityOptions;

        protected BaseOpenIdGrantHandler(
            OpenIddictApplicationManager<BTCPayOpenIdClient> applicationManager,
            OpenIddictAuthorizationManager<BTCPayOpenIdAuthorization> authorizationManager,
            SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions)
        {
            _applicationManager = applicationManager;
            _authorizationManager = authorizationManager;
            _signInManager = signInManager;
            _identityOptions = identityOptions;
        }


        protected Task<ClaimsPrincipal> CreateClaimsPrincipalAsync(OpenIdConnectRequest request, ApplicationUser user)
        {
            return OpenIdExtensions.CreateClaimsPrincipalAsync(_applicationManager, _authorizationManager,
                _identityOptions.Value, _signInManager, request, user);
        }
        public abstract ValueTask HandleAsync(T notification);
    }
}
