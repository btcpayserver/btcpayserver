using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Data;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Core;
using OpenIddict.Server;

namespace BTCPayServer.Authentication.OpenId
{
    public abstract class BaseOpenIdGrantHandler<T> : IOpenIddictServerEventHandler<T>
        where T : class, IOpenIddictServerEvent
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


        protected async Task<AuthenticationTicket> CreateTicketAsync(
            OpenIdConnectRequest request, ApplicationUser user,
            AuthenticationProperties properties = null)
        {
            return await OpenIdExtensions.CreateAuthenticationTicket(_applicationManager, _authorizationManager,
                _identityOptions.Value, _signInManager, request, user, properties);
        }

        public abstract Task<OpenIddictServerEventState> HandleAsync(T notification);
    }
}
