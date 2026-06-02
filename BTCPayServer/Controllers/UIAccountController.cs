using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Fido2;
using BTCPayServer.Fido2.Models;
using BTCPayServer.Filters;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Services;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.Impersonation;
using BTCPayServer.Security;
using Fido2NetLib;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIAccountController(
        UserLoginCodeService userLoginCodeService,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        SignInManager<ApplicationUser> signInManager,
        PoliciesSettings policiesSettings,
        SettingsRepository settingsRepository,
        Configuration.BTCPayServerOptions options,
        BTCPayServerEnvironment btcPayServerEnvironment,
        EventAggregator eventAggregator,
        Fido2Service fido2Service,
        LnurlAuthService lnurlAuthService,
        EmailSenderFactory emailSenderFactory,
        CallbackGenerator callbackGenerator,
        IStringLocalizer stringLocalizer,
        ViewLocalizer viewLocalizer,
        UserService userService,
        Logs logs)
        : Controller
    {
        readonly ILogger _logger = logs.PayServer;

        public PoliciesSettings PoliciesSettings { get; } = policiesSettings;
        public EmailSenderFactory EmailSenderFactory { get; } = emailSenderFactory;
        public IStringLocalizer StringLocalizer { get; } = stringLocalizer;
        public Logs Logs { get; } = logs;

        [TempData]
        public string ErrorMessage
        {
            get;
            set;
        }

        [HttpGet("/cheat/permissions")]
        [HttpGet("/cheat/permissions/stores/{storeId}")]
        [CheatModeRoute]
        public async Task<IActionResult> CheatPermissions([FromServices] IAuthorizationService authorizationService,
            [FromServices] PermissionService permissionService, string storeId = null)
        {
            var vm = new CheatPermissionsViewModel();
            vm.StoreId = storeId;
            var results = new System.Collections.Generic.List<(string, Task<AuthorizationResult>)>();
            foreach (var p in permissionService.Definitions.Values)
            {
                results.Add((p.Policy, authorizationService.AuthorizeAsync(User, storeId, new PolicyRequirement(p.Policy))));
                results.Add((p.Policy + ":", authorizationService.AuthorizeAsync(User, storeId, new PolicyRequirement(p.Policy, requireUnscoped: true))));
            }

            await Task.WhenAll(results.Select(r => r.Item2));
            results = results.OrderBy(r => r.Item1).ToList();
            vm.Permissions = results.Select(r => (r.Item1, r.Item2.Result)).ToArray();
            return View(vm);
        }

        [HttpGet("/login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login(string returnUrl = null, string email = null, string loginCode = null)
        {
            var allowRedirect =
                (email is null && User.Identity?.IsAuthenticated is true) ||
                (email is not null && User.FindFirst(ClaimTypes.Email)?.Value == email);

            if (allowRedirect)
                return RedirectToLocal(returnUrl);

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            if (!CanLoginOrRegister())
            {
                SetInsecureFlags();
            }

            var vm = new LoginViewModel { Email = email, LoginCode = loginCode };
            if (loginCode != null)
            {
                returnUrl = ParseLoginCodeUrl(vm, returnUrl);
                ViewData["ReturnUrl"] = returnUrl;
                var userId = userLoginCodeService.Verify(loginCode, false);
                vm.Email = (await userManager.FindByIdAsync(userId ?? ""))?.Email;
                return View("LoginWithLoginCode", vm);
            }
            else
            {
                ViewData["ReturnUrl"] = returnUrl;
                return View(nameof(Login), vm);
            }
        }

        private UserService.CanLoginContext CreateLoginContext(ApplicationUser user)
            => new(user, StringLocalizer, viewLocalizer, this.HttpContext.Request.GetRequestBaseUrl());

        [HttpPost("/login")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> Login(LoginViewModel model, string returnUrl = null)
        {
            if (!CanLoginOrRegister())
            {
                return RedirectToAction("Login");
            }

            ViewData["ReturnUrl"] = returnUrl;
            // Require the user to pass basic checks (approval, confirmed email, not disabled) before they can log on
            ApplicationUser user = null;
            bool bypass2fa = false;
            bool success = false;
            var session = new LoginSession()
            {
                RememberMe = model.RememberMe,
                AuthenticationMethod = model.Method,
                ReturnUrl = returnUrl
            };

            if (model.Method == "Passkey" && model.PasskeyResponse is not null && GetAssertionOptions("PASSKEY") is {} assertionOptions)
            {
                 var passKeyResult = await fido2Service.CompleteLogin(null, model.PasskeyResponse, assertionOptions, true);
                if (passKeyResult is Fido2Service.LoginResult.Success { User: { } u })
                {
                    user = u;
                    ModelState.Remove(nameof(model.Email));
                    model.Email = user.Email;
                    HttpContext.Session.Remove("PASSKEY");
                    bypass2fa = true;
                    success = true;
                }
                else if (passKeyResult is Fido2Service.LoginResult.Failed e)
                {
                  ModelState.AddModelError(string.Empty, e.Reason);
                  return View(model);
                }
            }

            if (model.Method == "Password")
            {
                user = await userManager.FindByEmailAsync(model.Email ?? "");
                success = user is not null && await userManager.CheckPasswordAsync(user, model.Password ?? "");
                if (!success && user is not null && !user.IsDisabledTemporarily)
                    await userManager.AccessFailedAsync(user);
            }

            if (model.Method == "LoginCode" && model.LoginCode is not null)
            {
                session.ReturnUrl = ParseLoginCodeUrl(model, session.ReturnUrl);
                session.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1.0);

                var userId = userLoginCodeService.Verify(model.LoginCode);
                user = await userManager.FindByIdAsync(userId ?? "");
                if (user is not null)
                {
                    success = true;
                    bypass2fa = true;
                }
            }

            if (user?.IsDisabledTemporarily is true)
                return LockoutView(user);

            var errorMessage = StringLocalizer["Invalid login attempt."].Value;
            if (!success || user is null)
            {
                ModelState.AddModelError(string.Empty, errorMessage!);
                return View(model);
            }

            if (bypass2fa)
            {
                return await RedirectLoginSuccess(user, session);
            }
            else if (model.Method == "Password")
            {
                // The password has already been checked.
                var hasTwofactor = await signInManager.IsTwoFactorEnabledAsync(user);
                if (hasTwofactor)
                {
                    session.Store(HttpContext.Session);
                    await signInManager.TwoFactorSignInAsync(user);
                    return RedirectToSecondaryLogin();
                }
                else
                {
                    return await RedirectLoginSuccess(user, session);
                }
            }
            return View(model);
        }

        private string ParseLoginCodeUrl(LoginViewModel model, string returnUrl)
        {
            // loginCode might be url: https://btcpay.example.com/login/code?loginCode=***&returnUrl=***
            // if that's the case, we need to extract the loginCode and the returnUrl from the query string.
            if (Uri.TryCreate(model.LoginCode, UriKind.Absolute, out var uri))
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
                if (query.TryGetValue("loginCode", out var code))
                {
                    model.LoginCode = code;
                }

                if (query.TryGetValue("returnUrl", out var url) && string.IsNullOrEmpty(returnUrl))
                {
                    returnUrl = url;
                    ViewData["ReturnUrl"] = returnUrl;
                }
            }

            return returnUrl;
        }

        public class LoginSession
        {
            public void Store(ISession session)
            {
                var str = JsonConvert.SerializeObject(this, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                session.SetString("LoginSession", str);
            }
            public static LoginSession Load(ISession session)
            {
                var str = session.GetString("LoginSession");
                if (str == null)
                    return new();
                return JsonConvert.DeserializeObject<LoginSession>(str);
            }

            public string AuthenticationMethod { get; set; } = AuthenticationSchemes.LimitedLogin;
            public bool RememberMe { get; set; }
            public string ReturnUrl { get; set; }
            [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
            public DateTimeOffset? ExpiresUtc { get; set; }

            public void Clear(ISession session)
            {
                session.Remove("LoginSession");
            }

            public AuthenticationProperties ToAuthenticationProperties(bool limitedLogin)
            => new()
            {
                ExpiresUtc = limitedLogin ? DateTimeOffset.UtcNow.AddMinutes(15) : ExpiresUtc,
                AllowRefresh = !limitedLogin,
                IsPersistent = RememberMe
            };
        }

        private RedirectToActionResult RedirectToSecondaryLogin()
        => RedirectToAction(nameof(SecondaryLogin));

        [HttpGet("login/second-login")]
        [AllowAnonymous]
        public async Task<IActionResult> SecondaryLogin()
        {
            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null)
                return Forbid();

            var vm = new SecondaryLoginViewModel();
            if (!btcPayServerEnvironment.IsSecure(HttpContext))
                return View("SecondaryLogin", vm);


            var fidoOptions = await fido2Service.RequestLogin(user.Id);
            if (fidoOptions is not null)
            {
                vm.LoginWithFido2ViewModel = new LoginWithFido2ViewModel()
                {
                    Data = fidoOptions.ToJson()
                };
                HttpContext.Session.SetString("FIDO", fidoOptions.ToJson());
            }

            var r = await lnurlAuthService.RequestLogin(user.Id);
            if (r is not null)
            {
                vm.LoginWithLNURLAuthViewModel = new LoginWithLNURLAuthViewModel
                {
                    LNURLEndpoint = new Uri(callbackGenerator.ForLNUrlAuth(user, r))
                };
            }

            if (await userManager.IsAuthenticatorConfigured(user))
            {
                vm.LoginWithAuthenticator = new LoginWithAuthenticatorModel();
            }
            return View("SecondaryLogin", vm);
        }

        [HttpPost("/login/lnurlauth")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWithLNURLAuth(LoginWithLNURLAuthViewModel viewModel)
        {
            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            if (!CanLoginOrRegister() || user is null)
            {
                return RedirectToAction("Login");
            }
            if (user.IsDisabledTemporarily)
                return LockoutView(user);
            var session = LoginSession.Load(HttpContext.Session);

            var errorMessage = StringLocalizer["Invalid login attempt."].Value;
            try
            {
                var k1 = Encoders.Hex.DecodeData(viewModel.LNURLEndpoint.ParseQueryString().Get("k1"));
                if (lnurlAuthService.FinalLoginStore.TryRemove(user.Id, out var storedk1) &&
                    storedk1.SequenceEqual(k1))
                {
                    lnurlAuthService.FinalLoginStore.TryRemove(user.Id, out _);
                    return await RedirectLoginSuccess(user, session);
                }
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = errorMessage
                });
            }
            return RedirectToSecondaryLogin();
        }

        [HttpPost("/login/passkey/options")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPasskeyOptions()
        {
            if (!CanLoginOrRegister())
                return BadRequest("Insecure connection");
            if (!btcPayServerEnvironment.IsSecure(HttpContext))
                return BadRequest("WebAuthn requires a secure connection");
            var o = await fido2Service.RequestLogin(null);
            if (o is null)
                return BadRequest("Passkey is not supported");
            HttpContext.Session.SetString("PASSKEY", o.ToJson());
            return Ok(o.ToJson());
        }

        AssertionOptions GetAssertionOptions(string key)
        {
            var result = HttpContext.Session.GetString(key);
            return result is null ? null : AssertionOptions.FromJson(result);
        }

        [HttpPost("/login/fido2")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWithFido2(LoginWithFido2ViewModel viewModel)
        {
            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            var assertionOptions = user is null ? null : GetAssertionOptions("FIDO");
            if (!CanLoginOrRegister() || assertionOptions is null)
            {
                return RedirectToAction("Login");
            }
            if (user.IsDisabledTemporarily)
                return LockoutView(user);

            var session = LoginSession.Load(HttpContext.Session);


            var errorMessage = "Invalid login attempt.";
            var loginResult = await fido2Service.CompleteLogin(user.Id, viewModel.Response, assertionOptions, false);
            if (loginResult is Fido2Service.LoginResult.Success)
            {
                HttpContext.Session.Remove("FIDO");
                return await RedirectLoginSuccess(user, session);
            }
            else if (loginResult is Fido2Service.LoginResult.Failed e)
            {
                errorMessage = e.Reason;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = errorMessage
                });
            }

            viewModel.Response = null;
            return RedirectToSecondaryLogin();
        }

        [HttpGet("/login/authenticator")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginWithAuthenticator()
        {
            if (!CanLoginOrRegister())
            {
                return RedirectToAction("Login");
            }

            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
                return Forbid();

            return RedirectToSecondaryLogin();
        }

        [HttpPost("/login/authenticator")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWithAuthenticator(LoginWithAuthenticatorModel model)
        {
            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            if (!CanLoginOrRegister() || user is null || !await userManager.IsAuthenticatorConfigured(user))
            {
                return RedirectToAction("Login");
            }
            if (user.IsDisabledTemporarily)
                return LockoutView(user);

            var session = LoginSession.Load(HttpContext.Session);

            var authenticatorCode = (model?.TwoFactorCode ?? "") .Replace(" ", string.Empty, StringComparison.InvariantCulture)
                .Replace("-", string.Empty, StringComparison.InvariantCulture);
            var success = await userManager.VerifyTwoFactorTokenAsync(user, signInManager.Options.Tokens.AuthenticatorTokenProvider, authenticatorCode);
            if (success)
            {
                return await RedirectLoginSuccess(user, session);
            }

            await userManager.AccessFailedAsync(user);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = StringLocalizer["Invalid authenticator code."]
            });

            return RedirectToSecondaryLogin();
        }

        private async Task<IActionResult> RedirectLoginSuccess(ApplicationUser user, LoginSession session)
        {
            await this.HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            session.Clear(HttpContext.Session);

            var loginContext = this.CreateLoginContext(user);
            if (!await userService.CanLogin(loginContext))
            {
                signInManager.AuthenticationScheme = AuthenticationSchemes.LimitedLogin;
                await signInManager.SignInAsync(user, session.ToAuthenticationProperties(true), session.AuthenticationMethod);

                if (loginContext.FailedRedirectUrl is { } url)
                    return Redirect(url);

                TempData.SetStatusLoginResult(loginContext);
                return RedirectToAction(nameof(Login), new { returnUrl = session.ReturnUrl });
            }
            else
            {
                await signInManager.SignInAsync(user, session.ToAuthenticationProperties(false), session.AuthenticationMethod);
            }
            await userManager.ResetAccessFailedCountAsync(user);
            return RedirectToLocal(session.ReturnUrl);
        }

        private IActionResult LockoutView(ApplicationUser user) => View("Lockout", user);

        [HttpGet("/register")]
        [AllowAnonymous]
        public IActionResult Register(string returnUrl = null)
        {
            if (!CanLoginOrRegister())
            {
                SetInsecureFlags();
            }

            if (PoliciesSettings.LockSubscription && !User.IsInRole(Roles.ServerAdmin))
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");

            if (!string.IsNullOrWhiteSpace(PoliciesSettings.RegisterPageRedirect))
            {
                return Redirect(HttpContext.Request.GetAbsoluteUri(PoliciesSettings.RegisterPageRedirect));
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost("/register")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model, string returnUrl = null)
        {
            if (!CanLoginOrRegister())
                return RedirectToAction(nameof(Register));
            var r = Register(returnUrl);
            if (r is not ViewResult)
                return r;

            var policies = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            if (ModelState.IsValid)
            {
                var anyAdmin = (await userManager.GetUsersInRoleAsync(Roles.ServerAdmin)).Any();
                var isFirstAdmin = !anyAdmin || (model.IsAdmin && options.CheatMode);
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    RequiresEmailConfirmation = policies.RequiresConfirmedEmail,
                    RequiresApproval = policies.RequiresUserApproval,
                    Created = DateTimeOffset.UtcNow,
                    Approved = isFirstAdmin // auto-approve first admin
                };
                var result = await userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    if (isFirstAdmin)
                    {
                        await roleManager.CreateAsync(new IdentityRole(Roles.ServerAdmin));
                        await userManager.AddToRoleAsync(user, Roles.ServerAdmin);
                        var settings = await settingsRepository.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();
                        settings.FirstRun = false;
                        await settingsRepository.UpdateSetting(settings);
                        await settingsRepository.FirstAdminRegistered(policies, options.UpdateUrl != null, options.DisableRegistration, Logs);
                        RegisteredAdmin = true;
                    }

                    eventAggregator.Publish(await UserEvent.Registered.Create(user, null, callbackGenerator));
                    RegisteredUserId = user.Id;

                    TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Account created."].Value;

                    return await RedirectLoginSuccess(user, new UIAccountController.LoginSession()
                    {
                        ReturnUrl = returnUrl,
                    });
                }
                else
                {
                    AddErrors(result);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        // Properties used by tests
        public string RegisteredUserId { get; set; }
        public bool RegisteredAdmin { get; set; }

        [HttpGet("/logout")]
        public async Task<IActionResult> Logout()
        {
            await signInManager.SignOutAsync();
            HttpContext.DeleteUserPrefsCookie();
            return RedirectToAction(nameof(Login));
        }

        [HttpGet("/register/confirm-email")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(string userId, string code)
        {
            if (userId == null || code == null)
            {
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var result = await userManager.ConfirmEmailAsync(user, code);
            if (!result.Succeeded)
                return View("Error", new ErrorViewModel()
                {
                    Error = result.Errors.FirstOrDefault()?.Code,
                    ErrorDescription = result.Errors.FirstOrDefault()?.Description
                });

            var approvalLink = callbackGenerator.ForApproval(user);
            eventAggregator.Publish(new UserEvent.ConfirmedEmail(user, approvalLink));

            var hasPassword = await userManager.HasPasswordAsync(user);
            if (hasPassword)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Message = StringLocalizer["Your email has been confirmed."].Value
                });
                return RedirectToAction(nameof(Login), new { email = user.Email });
            }

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Info,
                Message = StringLocalizer["Your email has been confirmed. Please set your password."].Value
            });
            return await RedirectToSetPassword(user);
        }

        [HttpGet("/login/forgot-password")]
        [AllowAnonymous]
        public ActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost("/login/forgot-password")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [RateLimitsFilter(ZoneLimits.ForgotPassword, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (ModelState.IsValid && await EmailSenderFactory.IsComplete())
            {
                var user = await userManager.FindByEmailAsync(model.Email);
                var loginContext = CreateLoginContext(user);
                if (!await userService.CanLogin(loginContext))
                {
                    return RedirectToAction(nameof(ForgotPasswordConfirmation));
                }

                var callbackUri = await callbackGenerator.ForPasswordReset(user);
                eventAggregator.Publish(new UserEvent.PasswordResetRequested(user, callbackUri));
                return RedirectToAction(nameof(ForgotPasswordConfirmation));
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpGet("/login/forgot-password/confirm")]
        [AllowAnonymous]
        public ActionResult ForgotPasswordConfirmation()
        {
            return View();
        }

        [HttpGet("/login/set-password")]
        [AllowAnonymous]
        public async Task<IActionResult> SetPassword(string code = null, string userId = null, string email = null)
        {
            if (code == null)
            {
                throw new ApplicationException("A code must be supplied for this action.");
            }

            var user = string.IsNullOrEmpty(userId) ? null : await userManager.FindByIdAsync(userId);
            var hasPassword = user != null && await userManager.HasPasswordAsync(user);
            if (!string.IsNullOrEmpty(userId))
            {
                email = user?.Email;
            }

            return View(new SetPasswordViewModel
            {
                Code = code,
                Email = email,
                EmailSetInternally = !string.IsNullOrEmpty(email),
                HasPassword = hasPassword
            });
        }

        [HttpPost("/login/set-password")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(SetPasswordViewModel model, string returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await userManager.FindByEmailAsync(model.Email);
            if (user is null)
                return RedirectToAction(nameof(Login));
            var hasPassword = await userManager.HasPasswordAsync(user);
            var needsInitialPassword = !await userManager.HasPasswordAsync(user);
            // Let unapproved users set a password. Otherwise, don't reveal that the user does not exist.
            var loginContext = CreateLoginContext(user);
            if (!await userService.CanLogin(loginContext) && !needsInitialPassword)
            {
                TempData.SetStatusLoginResult(loginContext);
                return RedirectToAction(nameof(Login));
            }

            var result = await userManager.ResetPasswordAsync(user!, model.Code, model.Password);
            if (result.Succeeded)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Message = hasPassword
                        ? StringLocalizer["Password successfully set."].Value
                        : StringLocalizer["Account successfully created."].Value
                });

                // see if we can sign in user after accepting an invitation and setting the password
                loginContext = CreateLoginContext(user);
                if (needsInitialPassword && await userService.CanLogin(loginContext))
                {
                    var signInResult = await signInManager.PasswordSignInAsync(user.Email!, model.Password, true, true);
                    await userManager.UnsetInvitationTokenAsync(user.Id);
                    if (signInResult.Succeeded)
                    {
                        return RedirectToLocal(returnUrl);
                    }
                }

                return RedirectToAction(nameof(Login));
            }

            AddErrors(result);
            model.HasPassword = await userManager.HasPasswordAsync(user);
            return View(model);
        }

        [AllowAnonymous]
        [HttpGet("/invite/{userId}/{code}")]
        public async Task<IActionResult> AcceptInvite(string userId, string code)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
            {
                return NotFound();
            }

            var user = await userManager.FindByInvitationTokenAsync(userId, Uri.UnescapeDataString(code));
            if (user == null)
            {
                return NotFound();
            }

            var requiresEmailConfirmation = user.RequiresEmailConfirmation && !user.EmailConfirmed;
            var requiresSetPassword = !await userManager.HasPasswordAsync(user);
            if (requiresEmailConfirmation)
            {
                return await RedirectToConfirmEmail(user);
            }

            if (requiresSetPassword)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Info,
                    Message = StringLocalizer["Invitation accepted. Please set your password."].Value
                });
                return await RedirectToSetPassword(user);
            }

            // Inform user that a password has been set on account creation
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Info,
                Message = StringLocalizer["Your password has been set by the user who invited you."].Value
            });
            await userManager.UnsetInvitationTokenAsync(user.Id);
            return RedirectToAction(nameof(Login), new { email = user.Email });
        }

        private async Task<IActionResult> RedirectToConfirmEmail(ApplicationUser user)
        {
            var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
            return RedirectToAction(nameof(ConfirmEmail), new { userId = user.Id, code });
        }

        private async Task<IActionResult> RedirectToSetPassword(ApplicationUser user)
        {
            var code = await userManager.GeneratePasswordResetTokenAsync(user);
            return RedirectToAction(nameof(SetPassword), new { userId = user.Id, email = user.Email, code });
        }

        #region Helpers

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        private IActionResult RedirectToLocal(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            // After login, if there is an app on "/", we should redirect to BTCPay explicit home route, and not to the app.
            if (PoliciesSettings.RootAppId is not null && PoliciesSettings.RootAppType is not null)
                return RedirectToAction(nameof(UIHomeController.Home), "UIHome");

            if (PoliciesSettings.DomainToAppMapping is { } mapping)
            {
                var matchedDomainMapping = mapping.FirstOrDefault(item =>
                    item.Domain.Equals(HttpContext.Request.Host.Host, StringComparison.InvariantCultureIgnoreCase));
                if (matchedDomainMapping is not null)
                    return RedirectToAction(nameof(UIHomeController.Home), "UIHome");
            }

            return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
        }

        private bool CanLoginOrRegister()
        {
            return btcPayServerEnvironment.IsDeveloping || btcPayServerEnvironment.IsSecure(HttpContext);
        }

        private void SetInsecureFlags()
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = StringLocalizer["You cannot login over an insecure connection. Please use HTTPS or Tor."].Value
            });

            ViewData["disabled"] = true;
        }

        #endregion
    }
}
