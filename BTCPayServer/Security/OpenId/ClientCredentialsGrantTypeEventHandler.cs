using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;
using OpenIddict.Server;

namespace BTCPayServer.Security.OpenId
{
    public class ClientCredentialsGrantTypeEventHandler :
        BaseOpenIdGrantHandler<OpenIddictServerEvents.HandleTokenRequestContext>
    {
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
   OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.HandleTokenRequestContext>()
               .UseScopedHandler<ClientCredentialsGrantTypeEventHandler>()
               .Build();
        private readonly OpenIddictApplicationManager<BTCPayOpenIdClient> _applicationManager;

        private readonly UserManager<ApplicationUser> _userManager;

        public ClientCredentialsGrantTypeEventHandler(
            OpenIddictApplicationManager<BTCPayOpenIdClient> applicationManager,
            OpenIddictAuthorizationManager<BTCPayOpenIdAuthorization> authorizationManager,
            SignInManager<ApplicationUser> signInManager,
            IOptions<IdentityOptions> identityOptions,
            UserManager<ApplicationUser> userManager) : base(applicationManager, authorizationManager, signInManager,
            identityOptions)
        {
            _applicationManager = applicationManager;
            _userManager = userManager;
        }

        public override async ValueTask HandleAsync(
            OpenIddictServerEvents.HandleTokenRequestContext notification)
        {
            var request = notification.Request;
            var context = notification;
            if (!request.IsClientCredentialsGrantType())
            {
                return;
            }

            var application = await _applicationManager.FindByClientIdAsync(request.ClientId);
            if (application == null)
            {
                context.Reject(
                    error: OpenIddictConstants.Errors.InvalidClient,
                    description: "The client application was not found in the database.");
                return;
            }

            var user = await _userManager.FindByIdAsync(application.ApplicationUserId);
            context.Principal = await CreateClaimsPrincipalAsync(request, user);
        }
    }
}
