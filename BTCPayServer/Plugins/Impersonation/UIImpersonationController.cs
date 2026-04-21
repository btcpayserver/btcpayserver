using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Impersonation.Views;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Plugins.Impersonation;

[Area(ImpersonationPlugin.Area)]
public class UIImpersonationController(
    UserLoginCodeService userLoginCodeService,
    ImpersonationContext impersonationContext,
    IStringLocalizer stringLocalizer,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ViewLocalizer viewLocalizer,
    UserService userService,
    LinkGenerator linkGenerator) : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;
    private UserService.CanLoginContext CreateLoginContext(ApplicationUser user)
        => new(user, StringLocalizer, viewLocalizer, this.HttpContext.Request.GetRequestBaseUrl());


    [HttpGet("server/users/{userId}/log-as-user")]
    public async Task<IActionResult> LogAsUser([FromServices] IAuthorizationService authorizationService, string userId, bool login = false)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return NotFound();

        if (!(await authorizationService.AuthorizeAsync(HttpContext.User, userId, ImpersonationPlugin.CanImpersonateUser)).Succeeded)
            return Forbid();

        if (login)
        {
            var loginCode = userLoginCodeService.Generate(user.Id);
            return Redirect(linkGenerator.LoginCodeLink(loginCode, null, true, HttpContext.Request.GetRequestBaseUrl()));
        }

        return View(new LogAsUserViewModel
        {
            Id = user.Id,
            Email = user.Email,
            ReturnUrl = Url.Action(nameof(UIServerController.ListUsers), "UIServer")
        });
    }

    [HttpGet("/account/login-codes")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
    public ActionResult LoginCodes()
    {
        return View();
    }

    // GET is for signin via the POS backend
    [HttpGet("/login/code")]
    [AllowAnonymous]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> LoginUsingCode(string loginCode, string returnUrl = null, bool? impersonate = null)
    {
        return await LoginCodeResult(loginCode, returnUrl, impersonate);
    }

    [HttpPost("/login/code")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> LoginWithCode(string loginCode, string returnUrl = null, bool? impersonate = null)
    {
        return await LoginCodeResult(loginCode, returnUrl, impersonate);
    }

    [HttpPost("/login/code/revert")]
    [AllowAnonymous]
    public IActionResult RevertImpersonation()
    {
        var impersonator = impersonationContext.Revert();
        return Login(email: impersonator?.FindFirst(ClaimTypes.Email)?.Value);
    }

    private async Task<IActionResult> LoginCodeResult(string loginCode, string returnUrl, bool? impersonate)
    {
        if (!string.IsNullOrEmpty(loginCode))
        {
            // loginCode might be url: https://btcpay.example.com/login/code?loginCode=***&returnUrl=***
            // if that's the case, we need to extract the loginCode and the returnUrl from the query string.
            if (Uri.TryCreate(loginCode, UriKind.Absolute, out var uri))
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
                if (query.TryGetValue("loginCode", out var code))
                {
                    loginCode = code;
                }
                if (query.TryGetValue("returnUrl", out var url) && string.IsNullOrEmpty(returnUrl))
                {
                    returnUrl = url;
                }
            }

            var userId = userLoginCodeService.Verify(loginCode);
            if (userId is null)
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Login code was invalid"].Value;
                return Login(returnUrl);
            }
            if (userId == User.GetIdOrNull())
                return Login(returnUrl);

            var user = await userManager.FindByIdAsync(userId);
            var loginContext = CreateLoginContext(user);
            if (!await userService.CanLogin(loginContext))
            {
                TempData.SetStatusLoginResult(loginContext);
                return Login(returnUrl);
            }

            var now = DateTimeOffset.UtcNow;
            var authProperties = new AuthenticationProperties
            {
                IssuedUtc = now,
                AllowRefresh = false,
                IsPersistent = true,
                ExpiresUtc = now.AddDays(1)
            };

            if (impersonate is true)
            {
                impersonationContext.StartImpersonation();
            }

            await signInManager.SignInAsync(user, authProperties, AuthenticationSchemes.Cookie);
        }

        return Login(returnUrl);
    }

    private IActionResult Login(string returnUrl = null, string email = null)
    {
        email ??= User.FindFirst(ClaimTypes.Email)?.Value;
        return RedirectToAction(nameof(UIAccountController.Login), "UIAccount", new { email, returnUrl });
    }
}
