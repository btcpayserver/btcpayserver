using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Models;
using BTCPayServer.Services.U2F;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace BTCPayServer.Authentication.OpenId
{
    public class PasswordGrantTypeEventHandler : BaseOpenIdGrantHandler<OpenIddictServerEvents.HandleTokenRequest>
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly U2FService _u2FService;

        public PasswordGrantTypeEventHandler(SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IOptions<IdentityOptions> identityOptions, U2FService u2FService) : base(signInManager, identityOptions)
        {
            _userManager = userManager;
            _u2FService = u2FService;
        }

        public override async Task<OpenIddictServerEventState> HandleAsync(
            OpenIddictServerEvents.HandleTokenRequest notification)
        {
            var request = notification.Context.Request;
            if (!request.IsPasswordGrantType())
            {
                // Allow other handlers to process the event.
                return OpenIddictServerEventState.Unhandled;
            }

            // Validate the user credentials.
            // Note: to mitigate brute force attacks, you SHOULD strongly consider
            // applying a key derivation function like PBKDF2 to slow down
            // the password validation process. You SHOULD also consider
            // using a time-constant comparer to prevent timing attacks.
            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null || await _u2FService.HasDevices(user.Id) ||
                !(await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true))
                    .Succeeded)
            {
                notification.Context.Reject(
                    error: OpenIddictConstants.Errors.InvalidGrant,
                    description: "The specified credentials are invalid.");
                // Don't allow other handlers to process the event.
                return OpenIddictServerEventState.Handled;
            }

            notification.Context.Validate(await CreateTicketAsync(request, user));
            // Don't allow other handlers to process the event.
            return OpenIddictServerEventState.Handled;
        }
    }
}