using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.U2F;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.Server;
using Microsoft.AspNetCore;

namespace BTCPayServer.Security.OpenId
{
    public class PasswordGrantTypeEventHandler : BaseOpenIdGrantHandler<OpenIddictServerEvents.HandleTokenRequestContext>
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NicolasDorier.RateLimits.RateLimitService _rateLimitService;
        private readonly U2FService _u2FService;

        public PasswordGrantTypeEventHandler(
            OpenIddictApplicationManager<BTCPayOpenIdClient> applicationManager,
            OpenIddictAuthorizationManager<BTCPayOpenIdAuthorization> authorizationManager,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            NicolasDorier.RateLimits.RateLimitService rateLimitService,
            IOptions<IdentityOptions> identityOptions, U2FService u2FService) : base(applicationManager,
            authorizationManager, signInManager, identityOptions)
        {
            _userManager = userManager;
            _rateLimitService = rateLimitService;
            _u2FService = u2FService;
        }

        public static OpenIddictServerHandlerDescriptor Descriptor { get; } = 
            OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.HandleTokenRequestContext>()
                        .UseScopedHandler<PasswordGrantTypeEventHandler>()
                        .Build();

        public override async ValueTask HandleAsync(
            OpenIddictServerEvents.HandleTokenRequestContext notification)
        {
            var request = notification.Request;
            if (!request.IsPasswordGrantType())
            {
                return;
            }

            var httpContext = notification.Transaction.GetHttpRequest().HttpContext;
            await _rateLimitService.Throttle(ZoneLimits.Login, httpContext.Connection.RemoteIpAddress.ToString(), httpContext.RequestAborted);
            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null || await _u2FService.HasDevices(user.Id) ||
                !(await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true))
                    .Succeeded)
            {
                notification.Reject(
                    error: OpenIddictConstants.Errors.InvalidGrant,
                    description: "The specified credentials are invalid.");
                return;
            }

            notification.Principal = await CreateClaimsPrincipalAsync(request, user);
        }
    }
}
