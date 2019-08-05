/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Authentication.OpenId;
using BTCPayServer.Authentication.OpenId.Models;
using BTCPayServer.Models;
using BTCPayServer.Models.Authorization;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.Server;

namespace BTCPayServer.Controllers
{
    public class AuthorizationController : Controller
    {
        private readonly OpenIddictApplicationManager<BTCPayOpenIdClient> _applicationManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly OpenIddictAuthorizationManager<BTCPayOpenIdAuthorization> _authorizationManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IOptions<IdentityOptions> _IdentityOptions;

        public AuthorizationController(
            OpenIddictApplicationManager<BTCPayOpenIdClient> applicationManager,
            SignInManager<ApplicationUser> signInManager,
            OpenIddictAuthorizationManager<BTCPayOpenIdAuthorization> authorizationManager,
            UserManager<ApplicationUser> userManager,
            IOptions<IdentityOptions> identityOptions)
        {
            _applicationManager = applicationManager;
            _signInManager = signInManager;
            _authorizationManager = authorizationManager;
            _userManager = userManager;
            _IdentityOptions = identityOptions;
        }

        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication), HttpGet("~/connect/authorize")]
        public async Task<IActionResult> Authorize(OpenIdConnectRequest request)
        {
            // Retrieve the application details from the database.
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId);
            
            if (application == null)
            {
                return View("Error",
                    new ErrorViewModel
                    {
                        Error = OpenIddictConstants.Errors.InvalidClient,
                        ErrorDescription =
                            "Details concerning the calling client application cannot be found in the database"
                    });
            }
            
            if (request.HasPrompt(OpenIdConnectConstants.Prompts.None))
            {
                var userId = _userManager.GetUserId(User);
                var authorizations =
                    await _authorizationManager.FindAsync(userId, request.ClientId, OpenIddictConstants.Statuses.Valid);
                if (!authorizations.IsEmpty)
                {
                    return await Authorize(request, "yes");
                }
            }
            // Flow the request_id to allow OpenIddict to restore
            // the original authorization request from the cache.
            return View(new AuthorizeViewModel
            {
                ApplicationName = await _applicationManager.GetDisplayNameAsync(application),
                RequestId = request.RequestId,
                Scope = request.Scope
            });
        }

        [Authorize(AuthenticationSchemes = Policies.CookieAuthentication), HttpPost("~/connect/authorize")]
        public async Task<IActionResult> Authorize(OpenIdConnectRequest request, string consent)
        {
            switch (consent.ToUpperInvariant())
            {
                case "YES":
                    var user = await _userManager.GetUserAsync(User);
                    if (user == null)
                    {
                        return View("Error",
                            new ErrorViewModel
                            {
                                Error = OpenIddictConstants.Errors.ServerError,
                                ErrorDescription = "An internal error has occurred"
                            });
                    }

                    // Create a new authentication ticket.
                    var ticket = await OpenIdExtensions.CreateAuthenticationTicket(_IdentityOptions.Value, _signInManager, request , user );

                    // Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
                    return SignIn(ticket.Principal, ticket.Properties, ticket.AuthenticationScheme);
                case "NO":
                    // Notify OpenIddict that the authorization grant has been denied by the resource owner
                    // to redirect the user agent to the client application using the appropriate response_mode.
                    return Forbid(OpenIddictServerDefaults.AuthenticationScheme);
            }

            return NoContent();
        }
    }
}
