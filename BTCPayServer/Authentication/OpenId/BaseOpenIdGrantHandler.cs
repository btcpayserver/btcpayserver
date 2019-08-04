using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Extensions;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.Server;

namespace BTCPayServer.Authentication.OpenId
{
    public abstract class BaseOpenIdGrantHandler<T> : IOpenIddictServerEventHandler<T>
        where T : class, IOpenIddictServerEvent
    {
        protected readonly SignInManager<ApplicationUser> _signInManager;
        protected readonly IOptions<IdentityOptions> _identityOptions;

        protected BaseOpenIdGrantHandler(SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions)
        {
            _signInManager = signInManager;
            _identityOptions = identityOptions;
        }
        
        

        protected async Task<AuthenticationTicket> CreateTicketAsync(
            OpenIdConnectRequest request, ApplicationUser user,
            AuthenticationProperties properties = null)
        {
            return await OpenIdExtensions.CreateAuthenticationTicket(_identityOptions.Value, _signInManager, request, user, properties);
        }

        public abstract Task<OpenIddictServerEventState> HandleAsync(T notification);
    }
}
