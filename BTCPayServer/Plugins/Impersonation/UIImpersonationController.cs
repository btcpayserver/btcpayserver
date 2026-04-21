using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
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
    IStringLocalizer stringLocalizer,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ViewLocalizer viewLocalizer,
    UserService userService) : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;
    private UserService.CanLoginContext CreateLoginContext(ApplicationUser user)
        => new(user, StringLocalizer, viewLocalizer, this.HttpContext.Request.GetRequestBaseUrl());

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
    public async Task<IActionResult> LoginUsingCode(string loginCode, string returnUrl = null)
    {
        return await LoginCodeResult(loginCode, returnUrl);
    }

    [HttpPost("/login/code")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> LoginWithCode(string loginCode, string returnUrl = null)
    {
        return await LoginCodeResult(loginCode, returnUrl);
    }

    private async Task<IActionResult> LoginCodeResult(string loginCode, string returnUrl)
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

            await signInManager.SignInAsync(user, authProperties, AuthenticationSchemes.Cookie);
        }

        return Login(returnUrl);
    }

    private IActionResult Login(string returnUrl = null, string email = null)
    {
        email ??= User.FindFirst(ClaimTypes.Email)?.Value;
        return RedirectToAction(nameof(UIAccountController.Login), "UIAccount", new { area = "", email, returnUrl });
    }
}
