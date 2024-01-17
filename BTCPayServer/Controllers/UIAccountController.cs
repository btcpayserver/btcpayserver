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
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Services;
using Fido2NetLib;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIAccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        readonly RoleManager<IdentityRole> _RoleManager;
        readonly Configuration.BTCPayServerOptions _Options;
        private readonly BTCPayServerEnvironment _btcPayServerEnvironment;
        readonly SettingsRepository _SettingsRepository;
        private readonly Fido2Service _fido2Service;
        private readonly LnurlAuthService _lnurlAuthService;
        private readonly LinkGenerator _linkGenerator;
        private readonly UserLoginCodeService _userLoginCodeService;
        private readonly EventAggregator _eventAggregator;
        readonly ILogger _logger;

        public PoliciesSettings PoliciesSettings { get; }
        public Logs Logs { get; }

        public UIAccountController(
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
            LinkGenerator linkGenerator,
            Logs logs)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            PoliciesSettings = policiesSettings;
            _SettingsRepository = settingsRepository;
            _RoleManager = roleManager;
            _Options = options;
            _btcPayServerEnvironment = btcPayServerEnvironment;
            _fido2Service = fido2Service;
            _lnurlAuthService = lnurlAuthService;
            _linkGenerator = linkGenerator;
            _userLoginCodeService = userLoginCodeService;
            _eventAggregator = eventAggregator;
            _logger = logs.PayServer;
            Logs = logs;
        }

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
        public async Task<IActionResult> Login(string returnUrl = null, string email = null)
        {
            if (User.Identity.IsAuthenticated && string.IsNullOrEmpty(returnUrl))
                return RedirectToLocal();
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            if (!CanLoginOrRegister())
            {
                SetInsecureFlags();
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View(nameof(Login), new LoginViewModel() { Email = email });
        }


        [HttpPost("/login/code")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]

        public async Task<IActionResult> LoginWithCode(string loginCode, string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(loginCode))
            {
                var userId = _userLoginCodeService.Verify(loginCode);
                if (userId is null)
                {
                    ModelState.AddModelError(string.Empty,
                        "Login code was invalid");
                    return await Login(returnUrl, null);
                }
                var user = await _userManager.FindByIdAsync(userId);

                _logger.LogInformation("User with ID {UserId} logged in with a login code.", user.Id);
                await _signInManager.SignInAsync(user, false, "LoginCode");
                return RedirectToLocal(returnUrl);
            }
            return await Login(returnUrl, null);
        }

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
                // Require the user to have a confirmed email before they can log on.
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user != null)
                {
                    if (user.RequiresEmailConfirmation && !await _userManager.IsEmailConfirmedAsync(user))
                    {
                        ModelState.AddModelError(string.Empty,
                                      "You must have a confirmed email to log in.");
                        return View(model);
                    }
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return View(model);
                }

                var fido2Devices = await _fido2Service.HasCredentials(user.Id);
                var lnurlAuthCredentials = await _lnurlAuthService.HasCredentials(user.Id);
                if (!await _userManager.IsLockedOutAsync(user) && (fido2Devices || lnurlAuthCredentials))
                {
                    if (await _userManager.CheckPasswordAsync(user, model.Password))
                    {
                        LoginWith2faViewModel twoFModel = null;

                        if (user.TwoFactorEnabled)
                        {
                            // we need to do an actual sign in attempt so that 2fa can function in next step
                            await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
                            twoFModel = new LoginWith2faViewModel
                            {
                                RememberMe = model.RememberMe
                            };
                        }

                        return View("SecondaryLogin", new SecondaryLoginViewModel()
                        {
                            LoginWith2FaViewModel = twoFModel,
                            LoginWithFido2ViewModel = fido2Devices ? await BuildFido2ViewModel(model.RememberMe, user) : null,
                            LoginWithLNURLAuthViewModel = lnurlAuthCredentials ? await BuildLNURLAuthViewModel(model.RememberMe, user) : null,
                        });
                    }
                    else
                    {
                        var incrementAccessFailedResult = await _userManager.AccessFailedAsync(user);
                        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                        return View(model);

                    }
                }

                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    _logger.LogInformation($"User '{user.Id}' logged in.");
                    return RedirectToLocal(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return View("SecondaryLogin", new SecondaryLoginViewModel()
                    {
                        LoginWith2FaViewModel = new LoginWith2faViewModel()
                        {
                            RememberMe = model.RememberMe
                        }
                    });
                }
                if (result.IsLockedOut)
                {
                    _logger.LogWarning($"User '{user.Id}' account locked out.");
                    return RedirectToAction(nameof(Lockout), new { user.LockoutEnd });
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return View(model);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        private async Task<LoginWithFido2ViewModel> BuildFido2ViewModel(bool rememberMe, ApplicationUser user)
        {
            if (_btcPayServerEnvironment.IsSecure(HttpContext))
            {
                var r = await _fido2Service.RequestLogin(user.Id);
                if (r is null)
                {
                    return null;
                }
                return new LoginWithFido2ViewModel()
                {
                    Data = r,
                    UserId = user.Id,
                    RememberMe = rememberMe
                };
            }
            return null;
        }


        private async Task<LoginWithLNURLAuthViewModel> BuildLNURLAuthViewModel(bool rememberMe, ApplicationUser user)
        {
            if (_btcPayServerEnvironment.IsSecure(HttpContext))
            {
                var r = await _lnurlAuthService.RequestLogin(user.Id);
                if (r is null)
                {
                    return null;
                }
                return new LoginWithLNURLAuthViewModel()
                {

                    RememberMe = rememberMe,
                    UserId = user.Id,
                    LNURLEndpoint = new Uri(_linkGenerator.GetUriByAction(
                    action: nameof(UILNURLAuthController.LoginResponse),
                    controller: "UILNURLAuth",
                    values: new { userId = user.Id, action = "login", tag = "login", k1 = Encoders.Hex.EncodeData(r) }, Request.Scheme, Request.Host, Request.PathBase))
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
            var user = await _userManager.FindByIdAsync(viewModel.UserId);

            if (user == null)
            {
                return NotFound();
            }

            var errorMessage = string.Empty;
            try
            {
                var k1 = Encoders.Hex.DecodeData(viewModel.LNURLEndpoint.ParseQueryString().Get("k1"));
                if (_lnurlAuthService.FinalLoginStore.TryRemove(viewModel.UserId, out var storedk1) &&
                    storedk1.SequenceEqual(k1))
                {
                    _lnurlAuthService.FinalLoginStore.TryRemove(viewModel.UserId, out _);
                    await _signInManager.SignInAsync(user, viewModel.RememberMe, "FIDO2");
                    _logger.LogInformation("User logged in.");
                    return RedirectToLocal(returnUrl);
                }

                errorMessage = "Invalid login attempt.";
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }

            ModelState.AddModelError(string.Empty, errorMessage);
            return View("SecondaryLogin", new SecondaryLoginViewModel()
            {

                LoginWithFido2ViewModel = (await _fido2Service.HasCredentials(user.Id)) ? await BuildFido2ViewModel(viewModel.RememberMe, user) : null,
                LoginWithLNURLAuthViewModel = viewModel,
                LoginWith2FaViewModel = !user.TwoFactorEnabled
                    ? null
                    : new LoginWith2faViewModel()
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
            var user = await _userManager.FindByIdAsync(viewModel.UserId);

            if (user == null)
            {
                return NotFound();
            }

            var errorMessage = string.Empty;
            try
            {
                if (await _fido2Service.CompleteLogin(viewModel.UserId, JObject.Parse(viewModel.Response).ToObject<AuthenticatorAssertionRawResponse>()))
                {
                    await _signInManager.SignInAsync(user, viewModel.RememberMe, "FIDO2");
                    _logger.LogInformation("User logged in.");
                    return RedirectToLocal(returnUrl);
                }

                errorMessage = "Invalid login attempt.";
            }
            catch (Fido2VerificationException e)
            {
                errorMessage = e.Message;
            }

            ModelState.AddModelError(string.Empty, errorMessage);
            viewModel.Response = null;
            return View("SecondaryLogin", new SecondaryLoginViewModel()
            {
                LoginWithFido2ViewModel = viewModel,
                LoginWithLNURLAuthViewModel = (await _lnurlAuthService.HasCredentials(user.Id)) ? await BuildLNURLAuthViewModel(viewModel.RememberMe, user) : null,
                LoginWith2FaViewModel = !user.TwoFactorEnabled
                    ? null
                    : new LoginWith2faViewModel()
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
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();

            if (user == null)
            {
                throw new ApplicationException($"Unable to load two-factor authentication user.");
            }

            ViewData["ReturnUrl"] = returnUrl;

            return View("SecondaryLogin", new SecondaryLoginViewModel()
            {
                LoginWith2FaViewModel = new LoginWith2faViewModel { RememberMe = rememberMe },
                LoginWithFido2ViewModel = (await _fido2Service.HasCredentials(user.Id)) ? await BuildFido2ViewModel(rememberMe, user) : null,
                LoginWithLNURLAuthViewModel = (await _lnurlAuthService.HasCredentials(user.Id)) ? await BuildLNURLAuthViewModel(rememberMe, user) : null,
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

            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var authenticatorCode = model.TwoFactorCode.Replace(" ", string.Empty, StringComparison.InvariantCulture).Replace("-", string.Empty, StringComparison.InvariantCulture);

            var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, rememberMe, model.RememberMachine);

            if (result.Succeeded)
            {
                _logger.LogInformation("User with ID {UserId} logged in with 2fa.", user.Id);
                return RedirectToLocal(returnUrl);
            }
            else if (result.IsLockedOut)
            {
                _logger.LogWarning("User with ID {UserId} account locked out.", user.Id);
                return RedirectToAction(nameof(Lockout), new { user.LockoutEnd });
            }
            else
            {
                _logger.LogWarning("Invalid authenticator code entered for user with ID {UserId}.", user.Id);
                ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
                return View("SecondaryLogin", new SecondaryLoginViewModel()
                {
                    LoginWith2FaViewModel = model,
                    LoginWithFido2ViewModel = (await _fido2Service.HasCredentials(user.Id)) ? await BuildFido2ViewModel(rememberMe, user) : null,
                    LoginWithLNURLAuthViewModel = (await _lnurlAuthService.HasCredentials(user.Id)) ? await BuildLNURLAuthViewModel(rememberMe, user) : null,
                });
            }
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
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
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

            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
            {
                throw new ApplicationException($"Unable to load two-factor authentication user.");
            }

            var recoveryCode = model.RecoveryCode.Replace(" ", string.Empty, StringComparison.InvariantCulture);

            var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

            if (result.Succeeded)
            {
                _logger.LogInformation("User with ID {UserId} logged in with a recovery code.", user.Id);
                return RedirectToLocal(returnUrl);
            }
            if (result.IsLockedOut)
            {
                _logger.LogWarning("User with ID {UserId} account locked out.", user.Id);

                return RedirectToAction(nameof(Lockout), new { user.LockoutEnd });
            }
            else
            {
                _logger.LogWarning("Invalid recovery code entered for user with ID {UserId}", user.Id);
                ModelState.AddModelError(string.Empty, "Invalid recovery code entered.");
                return View();
            }
        }

        [HttpGet("/login/lockout")]
        [AllowAnonymous]
        public IActionResult Lockout(DateTimeOffset? lockoutEnd)
        {
            return View(lockoutEnd);
        }

        [HttpGet("/register")]
        [AllowAnonymous]
        [RateLimitsFilter(ZoneLimits.Register, Scope = RateLimitsScope.RemoteAddress)]
        public IActionResult Register(string returnUrl = null, bool logon = true)
        {
            if (!CanLoginOrRegister())
            {
                SetInsecureFlags();
            }
            if (PoliciesSettings.LockSubscription && !User.IsInRole(Roles.ServerAdmin))
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost("/register")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model, string returnUrl = null, bool logon = true)
        {
            if (!CanLoginOrRegister())
            {
                return RedirectToAction("Register");
            }

            ViewData["ReturnUrl"] = returnUrl;
            ViewData["Logon"] = logon.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
            var policies = await _SettingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            if (policies.LockSubscription && !User.IsInRole(Roles.ServerAdmin))
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    RequiresEmailConfirmation = policies.RequiresConfirmedEmail,
                    Created = DateTimeOffset.UtcNow
                };
                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    var admin = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
                    if (admin.Count == 0 || (model.IsAdmin && _Options.CheatMode))
                    {
                        await _RoleManager.CreateAsync(new IdentityRole(Roles.ServerAdmin));
                        await _userManager.AddToRoleAsync(user, Roles.ServerAdmin);
                        var settings = await _SettingsRepository.GetSettingAsync<ThemeSettings>();
                        settings.FirstRun = false;
                        await _SettingsRepository.UpdateSetting<ThemeSettings>(settings);

                        await _SettingsRepository.FirstAdminRegistered(policies, _Options.UpdateUrl != null, _Options.DisableRegistration, Logs);
                        RegisteredAdmin = true;
                    }

                    _eventAggregator.Publish(new UserRegisteredEvent()
                    {
                        RequestUri = Request.GetAbsoluteRootUri(),
                        User = user,
                        Admin = RegisteredAdmin
                    });
                    RegisteredUserId = user.Id;

                    if (!policies.RequiresConfirmedEmail)
                    {
                        if (logon)
                            await _signInManager.SignInAsync(user, isPersistent: false);
                        return RedirectToLocal(returnUrl);
                    }
                    else
                    {
                        TempData[WellKnownTempData.SuccessMessage] = "Account created, please confirm your email";
                        return View();
                    }
                }
                AddErrors(result);
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
            await _signInManager.SignOutAsync();
            HttpContext.DeleteUserPrefsCookie();
            _logger.LogInformation("User logged out.");
            return RedirectToAction(nameof(UIAccountController.Login));
        }

        [HttpGet("/register/confirm-email")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(string userId, string code)
        {
            if (userId == null || code == null)
            {
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
            }
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{userId}'.");
            }

            var result = await _userManager.ConfirmEmailAsync(user, code);
            if (!await _userManager.HasPasswordAsync(user))
            {

                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Info,
                    Message = "Your email has been confirmed but you still need to set your password."
                });
                return RedirectToAction("SetPassword", new { email = user.Email, code = await _userManager.GeneratePasswordResetTokenAsync(user) });
            }

            if (result.Succeeded)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Message = "Your email has been confirmed."
                });
                return RedirectToAction("Login", new { email = user.Email });
            }

            return View("Error");
        }

        [HttpGet("/login/forgot-password")]
        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost("/login/forgot-password")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [RateLimitsFilter(ZoneLimits.ForgotPassword, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user == null || (user.RequiresEmailConfirmation && !(await _userManager.IsEmailConfirmedAsync(user))))
                {
                    // Don't reveal that the user does not exist or is not confirmed
                    return RedirectToAction(nameof(ForgotPasswordConfirmation));
                }
                _eventAggregator.Publish(new UserPasswordResetRequestedEvent()
                {
                    User = user,
                    RequestUri = Request.GetAbsoluteRootUri()
                });
                return RedirectToAction(nameof(ForgotPasswordConfirmation));
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpGet("/login/forgot-password/confirm")]
        [AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation()
        {
            return View();
        }

        [HttpGet("/login/set-password")]
        [AllowAnonymous]
        public async Task<IActionResult> SetPassword(string code = null, string userId = null, string email = null)
        {
            if (code == null)
            {
                throw new ApplicationException("A code must be supplied for password reset.");
            }

            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _userManager.FindByIdAsync(userId);
                email = user?.Email;
            }

            var model = new SetPasswordViewModel { Code = code, Email = email, EmailSetInternally = !string.IsNullOrEmpty(email) };
            return View(model);
        }

        [HttpPost("/login/set-password")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                // Don't reveal that the user does not exist
                return RedirectToAction(nameof(Login));
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Code, model.Password);
            if (result.Succeeded)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Message = "Password successfully set."
                });
                return RedirectToAction(nameof(Login));
            }

            AddErrors(result);
            return View(model);
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
            return _btcPayServerEnvironment.IsDeveloping || _btcPayServerEnvironment.IsSecure(HttpContext);
        }

        private void SetInsecureFlags()
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = "You cannot login over an insecure connection. Please use HTTPS or Tor."
            });

            ViewData["disabled"] = true;
        }

        #endregion
    }
}
