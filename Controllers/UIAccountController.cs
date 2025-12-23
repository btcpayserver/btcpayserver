using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
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
using Fido2NetLib;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIAccountController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        SignInManager<ApplicationUser> signInManager,
        PoliciesSettings policiesSettings,
        SettingsRepository settingsRepository,
        Configuration.BTCPayServerOptions options,
        BTCPayServerEnvironment btcPayServerEnvironment,
        EventAggregator eventAggregator,
        Fido2Service fido2Service,
        UserLoginCodeService userLoginCodeService,
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
            get; set;
        }

        [HttpGet("/cheat/permissions")]
        [HttpGet("/cheat/permissions/stores/{storeId}")]
        [CheatModeRoute]
        public async Task<IActionResult> CheatPermissions([FromServices] IAuthorizationService authorizationService, string storeId = null)
        {
            var vm = new CheatPermissionsViewModel();
            vm.StoreId = storeId;
            var results = new System.Collections.Generic.List<(string, Task<AuthorizationResult>)>();
            foreach (var p in Policies.AllPolicies.Concat(new[] { Policies.CanModifyStoreSettingsUnscoped }))
            {
                results.Add((p, authorizationService.AuthorizeAsync(User, storeId, p)));
            }
            await Task.WhenAll(results.Select(r => r.Item2));
            results = results.OrderBy(r => r.Item1).ToList();
            vm.Permissions = results.Select(r => (r.Item1, r.Item2.Result)).ToArray();
            return View(vm);
        }

        [HttpGet("/login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login(string returnUrl = null, string email = null, bool allowLimitedLogin = false)
        {
            if (User.Identity?.IsAuthenticated is true && string.IsNullOrEmpty(returnUrl))
                return RedirectToLocal();

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            if (!CanLoginOrRegister())
            {
                SetInsecureFlags();
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View(nameof(Login), new LoginViewModel { Email = email, AllowLimitedLogin = allowLimitedLogin });
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
                var code = loginCode.Split(';').First();
                var userId = userLoginCodeService.Verify(code);
                if (userId is null)
                {
                    TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Login code was invalid"].Value;
                    return await Login(returnUrl);
                }

                var user = await userManager.FindByIdAsync(userId);
                var loginContext = CreateLoginContext(user);
                if (!await userService.CanLogin(loginContext))
                {
                    TempData.SetStatusLoginResult(loginContext);
                    return await Login(returnUrl);
                }

                _logger.LogInformation("User {Email} logged in with a login code", user!.Email);
                await signInManager.SignInAsync(user, false, "LoginCode");
                return RedirectToLocal(returnUrl);
            }
            return await Login(returnUrl);
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
            if (ModelState.IsValid)
            {
                // Require the user to pass basic checks (approval, confirmed email, not disabled) before they can log on
                var user = await userManager.FindByEmailAsync(model.Email);
                var errorMessage = StringLocalizer["Invalid login attempt."].Value;
                var loginContext = CreateLoginContext(user);
                if (!await userService.CanLogin(loginContext))
                {
                    if (user is null || !await userManager.CheckPasswordAsync(user, model.Password))
                    {
                        if (user is not null)
                            await userManager.AccessFailedAsync(user);
                        ModelState.AddModelError(string.Empty, errorMessage!);
                        return View(model);
                    }
                    // Only show the real reason if the user has input the right password...
                    else
                    {
                        var principal = await signInManager.CreateUserPrincipalAsync(user);
                        await HttpContext.SignInAsync(AuthenticationSchemes.LimitedLogin, principal);
                        if (model.AllowLimitedLogin && returnUrl != null)
                            return RedirectToLocal(returnUrl);

                        if (loginContext.FailedRedirectUrl is { } url)
                            return Redirect(url);
                        else
                            TempData.SetStatusLoginResult(loginContext);
                        return RedirectToAction(nameof(Login));
                    }
                }

                var fido2Devices = await fido2Service.HasCredentials(user!.Id);
                var lnurlAuthCredentials = await lnurlAuthService.HasCredentials(user.Id);
                if (fido2Devices || lnurlAuthCredentials)
                {
                    if (await userManager.CheckPasswordAsync(user, model.Password))
                    {
                        LoginWith2faViewModel twoFModel = null;

                        if (user.TwoFactorEnabled)
                        {
                            // we need to do an actual sign in attempt so that 2fa can function in next step
                            await signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
                            twoFModel = new LoginWith2faViewModel
                            {
                                RememberMe = model.RememberMe
                            };
                        }

                        return View("SecondaryLogin", new SecondaryLoginViewModel
                        {
                            LoginWith2FaViewModel = twoFModel,
                            LoginWithFido2ViewModel = fido2Devices ? await BuildFido2ViewModel(model.RememberMe, user) : null,
                            LoginWithLNURLAuthViewModel = lnurlAuthCredentials ? await BuildLNURLAuthViewModel(model.RememberMe, user) : null,
                        });
                    }

                    await userManager.AccessFailedAsync(user);
                    ModelState.AddModelError(string.Empty, errorMessage!);
                    return View(model);
                }

                var result = await signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    _logger.LogInformation("User {Email} logged in", user.Email);
                    return RedirectToLocal(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return View("SecondaryLogin", new SecondaryLoginViewModel
                    {
                        LoginWith2FaViewModel = new LoginWith2faViewModel
                        {
                            RememberMe = model.RememberMe
                        }
                    });
                }
                if (result.IsLockedOut)
                {
                    _logger.LogWarning("User {Email} tried to log in, but is locked out", user.Email);
                    return RedirectToAction(nameof(Lockout), new { user.LockoutEnd });
                }

                ModelState.AddModelError(string.Empty, errorMessage);
                return View(model);
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        private async Task<LoginWithFido2ViewModel> BuildFido2ViewModel(bool rememberMe, ApplicationUser user)
        {
            if (btcPayServerEnvironment.IsSecure(HttpContext))
            {
                var r = await fido2Service.RequestLogin(user.Id);
                if (r is null)
                {
                    return null;
                }
                return new LoginWithFido2ViewModel
                {
                    Data = System.Text.Json.JsonSerializer.Serialize(r, r.GetType()),
                    UserId = user.Id,
                    RememberMe = rememberMe
                };
            }
            return null;
        }

        private async Task<LoginWithLNURLAuthViewModel> BuildLNURLAuthViewModel(bool rememberMe, ApplicationUser user)
        {
            if (btcPayServerEnvironment.IsSecure(HttpContext))
            {
                var r = await lnurlAuthService.RequestLogin(user.Id);
                if (r is null)
                {
                    return null;
                }
                return new LoginWithLNURLAuthViewModel
                {
                    RememberMe = rememberMe,
                    UserId = user.Id,
                    LNURLEndpoint = new Uri(callbackGenerator.ForLNUrlAuth(user, r))
                };
            }
            return null;
        }

        [HttpPost("/login/lnurlauth")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWithLNURLAuth(LoginWithLNURLAuthViewModel viewModel, string returnUrl = null)
        {
            if (!CanLoginOrRegister())
            {
                return RedirectToAction("Login");
            }

            ViewData["ReturnUrl"] = returnUrl;
            var errorMessage = StringLocalizer["Invalid login attempt."].Value;
            var user = await userManager.FindByIdAsync(viewModel.UserId);
            var loggingContext = CreateLoginContext(user);
            if (!await userService.CanLogin(loggingContext))
            {
                TempData.SetStatusLoginResult(loggingContext);
                return RedirectToAction("Login");
            }

            try
            {
                var k1 = Encoders.Hex.DecodeData(viewModel.LNURLEndpoint.ParseQueryString().Get("k1"));
                if (lnurlAuthService.FinalLoginStore.TryRemove(viewModel.UserId, out var storedk1) &&
                    storedk1.SequenceEqual(k1))
                {
                    lnurlAuthService.FinalLoginStore.TryRemove(viewModel.UserId, out _);
                    await signInManager.SignInAsync(user!, viewModel.RememberMe, "FIDO2");
                    _logger.LogInformation("User logged in");
                    return RedirectToLocal(returnUrl);
                }
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                ModelState.AddModelError(string.Empty, errorMessage);
            }
            return View("SecondaryLogin", new SecondaryLoginViewModel
            {
                LoginWithFido2ViewModel = await fido2Service.HasCredentials(user!.Id) ? await BuildFido2ViewModel(viewModel.RememberMe, user) : null,
                LoginWithLNURLAuthViewModel = viewModel,
                LoginWith2FaViewModel = !user.TwoFactorEnabled
                    ? null
                    : new LoginWith2faViewModel
                    {
                        RememberMe = viewModel.RememberMe
                    }
            });
        }

        [HttpPost("/login/fido2")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWithFido2(LoginWithFido2ViewModel viewModel, string returnUrl = null)
        {
            if (!CanLoginOrRegister())
            {
                return RedirectToAction("Login");
            }

            ViewData["ReturnUrl"] = returnUrl;
            var errorMessage = "Invalid login attempt.";
            var user = await userManager.FindByIdAsync(viewModel.UserId);
            var loginContext = CreateLoginContext(user);
            if (!await userService.CanLogin(loginContext))
            {
                TempData.SetStatusLoginResult(loginContext);
                return RedirectToAction("Login");
            }

            try
            {
                if (await fido2Service.CompleteLogin(viewModel.UserId, System.Text.Json.JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(viewModel.Response)))
                {
                    await signInManager.SignInAsync(user!, viewModel.RememberMe, "FIDO2");
                    _logger.LogInformation("User {Email} logged in with FIDO2", user.Email);
                    return RedirectToLocal(returnUrl);
                }
            }
            catch (Fido2VerificationException e)
            {
                errorMessage = e.Message;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                ModelState.AddModelError(string.Empty, errorMessage);
            }
            viewModel.Response = null;
            return View("SecondaryLogin", new SecondaryLoginViewModel
            {
                LoginWithFido2ViewModel = viewModel,
                LoginWithLNURLAuthViewModel = await lnurlAuthService.HasCredentials(user!.Id) ? await BuildLNURLAuthViewModel(viewModel.RememberMe, user) : null,
                LoginWith2FaViewModel = !user.TwoFactorEnabled
                    ? null
                    : new LoginWith2faViewModel
                    {
                        RememberMe = viewModel.RememberMe
                    }
            });
        }

        [HttpGet("/login/2fa")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginWith2fa(bool rememberMe, string returnUrl = null)
        {
            if (!CanLoginOrRegister())
            {
                return RedirectToAction("Login");
            }

            // Ensure the user has gone through the username & password screen first
            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                throw new ApplicationException($"Unable to load two-factor authentication user.");
            }

            ViewData["ReturnUrl"] = returnUrl;

            return View("SecondaryLogin", new SecondaryLoginViewModel
            {
                LoginWith2FaViewModel = new LoginWith2faViewModel { RememberMe = rememberMe },
                LoginWithFido2ViewModel = await fido2Service.HasCredentials(user.Id) ? await BuildFido2ViewModel(rememberMe, user) : null,
                LoginWithLNURLAuthViewModel = await lnurlAuthService.HasCredentials(user.Id) ? await BuildLNURLAuthViewModel(rememberMe, user) : null,
            });
        }

        [HttpPost("/login/2fa")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWith2fa(LoginWith2faViewModel model, bool rememberMe, string returnUrl = null)
        {
            if (!CanLoginOrRegister())
            {
                return RedirectToAction("Login");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            var loginContext = CreateLoginContext(user);
            if (!await userService.CanLogin(loginContext))
            {
                TempData.SetStatusLoginResult(loginContext);
                return View(model);
            }

            var authenticatorCode = model.TwoFactorCode.Replace(" ", string.Empty, StringComparison.InvariantCulture).Replace("-", string.Empty, StringComparison.InvariantCulture);
            var result = await signInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, rememberMe, model.RememberMachine);
            if (result.Succeeded)
            {
                _logger.LogInformation("User {Email} logged in with 2FA", user.Email);
                return RedirectToLocal(returnUrl);
            }

            _logger.LogWarning("User {Email} entered invalid authenticator code", user.Email);
            ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
            return View("SecondaryLogin", new SecondaryLoginViewModel
            {
                LoginWith2FaViewModel = model,
                LoginWithFido2ViewModel = await fido2Service.HasCredentials(user.Id) ? await BuildFido2ViewModel(rememberMe, user) : null,
                LoginWithLNURLAuthViewModel = await lnurlAuthService.HasCredentials(user.Id) ? await BuildLNURLAuthViewModel(rememberMe, user) : null,
            });
        }

        [HttpGet("/login/recovery-code")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginWithRecoveryCode(string returnUrl = null)
        {
            if (!CanLoginOrRegister())
            {
                return RedirectToAction("Login");
            }

            // Ensure the user has gone through the username & password screen first
            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                throw new ApplicationException($"Unable to load two-factor authentication user.");
            }

            ViewData["ReturnUrl"] = returnUrl;

            return View();
        }

        [HttpPost("/login/recovery-code")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWithRecoveryCode(LoginWithRecoveryCodeViewModel model, string returnUrl = null)
        {
            if (!CanLoginOrRegister())
            {
                return RedirectToAction("Login");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
            var loginContext = CreateLoginContext(user);
            if (!await userService.CanLogin(loginContext))
            {
                TempData.SetStatusLoginResult(loginContext);
                return View(model);
            }

            var recoveryCode = model.RecoveryCode.Replace(" ", string.Empty, StringComparison.InvariantCulture);
            var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
            if (result.Succeeded)
            {
                _logger.LogInformation("User {Email} logged in with a recovery code", user.Email);
                return RedirectToLocal(returnUrl);
            }
            if (result.IsLockedOut)
            {
                _logger.LogWarning("User {Email} account locked out", user.Email);

                return RedirectToAction(nameof(Lockout), new { user.LockoutEnd });
            }

            _logger.LogWarning("User {Email} entered invalid recovery code", user.Email);
            ModelState.AddModelError(string.Empty, "Invalid recovery code entered.");
            return View();
        }

        [HttpGet("/login/lockout")]
        [AllowAnonymous]
        public IActionResult Lockout(DateTimeOffset? lockoutEnd)
        {
            return View(lockoutEnd);
        }

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
        public async Task<IActionResult> Register(RegisterViewModel model, string returnUrl = null, bool logon = true)
        {
            if (!CanLoginOrRegister())
                return RedirectToAction(nameof(Register));
            var r = Register(returnUrl);
            if (r is not ViewResult)
                return r;

            ViewData["Logon"] = logon.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
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

                    var ctx = CreateLoginContext(user);
                    if (!await userService.CanLogin(ctx))
                    {
                        if (ctx.FailedRedirectUrl is { } url)
                        {
                            return Redirect(url);
                        }
                        else
                        {
                            TempData.SetStatusLoginResult(ctx);
                            return RedirectToAction(nameof(Login));
                        }
                    }

                    if (logon)
                    {
                        await signInManager.SignInAsync(user, isPersistent: false);
                        _logger.LogInformation("User {Email} logged in", user.Email);
                        return RedirectToLocal(returnUrl);
                    }
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
            var userId = signInManager.UserManager.GetUserId(HttpContext.User);
            var user = await userManager.FindByIdAsync(userId);
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
