using System;
using System.Globalization;
using System.Security.Policy;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.U2F;
using BTCPayServer.U2F.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NicolasDorier.RateLimits;
using U2F.Core.Exceptions;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Route("[controller]/[action]")]
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        readonly RoleManager<IdentityRole> _RoleManager;
        readonly SettingsRepository _SettingsRepository;
        readonly Configuration.BTCPayServerOptions _Options;
        private readonly BTCPayServerEnvironment _btcPayServerEnvironment;
        public U2FService _u2FService;
        private readonly RateLimitService _rateLimitService;
        private readonly EventAggregator _eventAggregator;
        readonly ILogger _logger;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            SignInManager<ApplicationUser> signInManager,
            SettingsRepository settingsRepository,
            Configuration.BTCPayServerOptions options,
            BTCPayServerEnvironment btcPayServerEnvironment,
            U2FService u2FService,
            RateLimitService rateLimitService,
            EventAggregator eventAggregator)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _RoleManager = roleManager;
            _SettingsRepository = settingsRepository;
            _Options = options;
            _btcPayServerEnvironment = btcPayServerEnvironment;
            _u2FService = u2FService;
            _rateLimitService = rateLimitService;
            _eventAggregator = eventAggregator;
            _logger = Logs.PayServer;
        }

        [TempData]
        public string ErrorMessage
        {
            get; set;
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("~/login", Order = 1)]
        [Route("~/Account/Login", Order = 2)]
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
            return View(new LoginViewModel()
            {
                Email = email
            });
        }


        [HttpPost]
        [AllowAnonymous]
        [Route("~/login", Order = 1)]
        [Route("~/Account/Login", Order = 2)]
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

                if (!await _userManager.IsLockedOutAsync(user) && await _u2FService.HasDevices(user.Id))
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
                            LoginWithU2FViewModel = await BuildU2FViewModel(model.RememberMe, user)
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
                    _logger.LogInformation("User logged in.");
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
                    _logger.LogWarning("User account locked out.");
                    return RedirectToAction(nameof(Lockout));
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

        private async Task<LoginWithU2FViewModel> BuildU2FViewModel(bool rememberMe, ApplicationUser user)
        {
            if (_btcPayServerEnvironment.IsSecure)
            {
                var u2fChallenge = await _u2FService.GenerateDeviceChallenges(user.Id,
                    Request.GetAbsoluteUriNoPathBase().ToString().TrimEnd('/'));

                return new LoginWithU2FViewModel()
                {
                    Version = u2fChallenge[0].version,
                    Challenge = u2fChallenge[0].challenge,
                    Challenges = u2fChallenge,
                    AppId = u2fChallenge[0].appId,
                    UserId = user.Id,
                    RememberMe = rememberMe
                };
            }

            return null;
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWithU2F(LoginWithU2FViewModel viewModel, string returnUrl = null)
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
                if (await _u2FService.AuthenticateUser(viewModel.UserId, viewModel.DeviceResponse))
                {
                    await _signInManager.SignInAsync(user, viewModel.RememberMe, "U2F");
                    _logger.LogInformation("User logged in.");
                    return RedirectToLocal(returnUrl);
                }

                errorMessage = "Invalid login attempt.";
            }
            catch (U2fException e)
            {
                errorMessage = e.Message;
            }

            ModelState.AddModelError(string.Empty, errorMessage);
            return View("SecondaryLogin", new SecondaryLoginViewModel()
            {
                LoginWithU2FViewModel = viewModel,
                LoginWith2FaViewModel = !user.TwoFactorEnabled
                    ? null
                    : new LoginWith2faViewModel()
                    {
                        RememberMe = viewModel.RememberMe
                    }
            });
        }

        [HttpGet]
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
                LoginWithU2FViewModel = (await _u2FService.HasDevices(user.Id)) ? await BuildU2FViewModel(rememberMe, user) : null
            });
        }

        [HttpPost]
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
                return RedirectToAction(nameof(Lockout));
            }
            else
            {
                _logger.LogWarning("Invalid authenticator code entered for user with ID {UserId}.", user.Id);
                ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
                return View("SecondaryLogin", new SecondaryLoginViewModel()
                {
                    LoginWith2FaViewModel = model,
                    LoginWithU2FViewModel = (await _u2FService.HasDevices(user.Id)) ? await BuildU2FViewModel(rememberMe, user) : null
                });
            }
        }

        [HttpGet]
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

        [HttpPost]
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
                return RedirectToAction(nameof(Lockout));
            }
            else
            {
                _logger.LogWarning("Invalid recovery code entered for user with ID {UserId}", user.Id);
                ModelState.AddModelError(string.Empty, "Invalid recovery code entered.");
                return View();
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Lockout()
        {
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("~/register", Order = 1)]
        [Route("~/Account/Register", Order = 2)]
        [RateLimitsFilter(ZoneLimits.Register, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> Register(string returnUrl = null, bool logon = true)
        {
            if (!CanLoginOrRegister())
            {
                SetInsecureFlags();
            }
            var policies = await _SettingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            if (policies.LockSubscription && !User.IsInRole(Roles.ServerAdmin))
                return RedirectToAction(nameof(HomeController.Index), "Home");
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["AllowIsAdmin"] = _Options.AllowAdminRegistration;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [Route("~/register", Order = 1)]
        [Route("~/Account/Register", Order = 2)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model, string returnUrl = null, bool logon = true)
        {
            if (!CanLoginOrRegister())
            {
                return RedirectToAction("Register");
            }

            ViewData["ReturnUrl"] = returnUrl;
            ViewData["Logon"] = logon.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
            ViewData["AllowIsAdmin"] = _Options.AllowAdminRegistration;
            var policies = await _SettingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            if (policies.LockSubscription && !User.IsInRole(Roles.ServerAdmin))
                return RedirectToAction(nameof(HomeController.Index), "Home");
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser { UserName = model.Email, Email = model.Email, RequiresEmailConfirmation = policies.RequiresConfirmedEmail, 
                    Created = DateTimeOffset.UtcNow };
                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    var admin = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
                    if (admin.Count == 0 || (model.IsAdmin && _Options.AllowAdminRegistration))
                    {
                        await _RoleManager.CreateAsync(new IdentityRole(Roles.ServerAdmin));
                        await _userManager.AddToRoleAsync(user, Roles.ServerAdmin);
                        var settings = await _SettingsRepository.GetSettingAsync<ThemeSettings>();
                        settings.FirstRun = false;
                        await _SettingsRepository.UpdateSetting<ThemeSettings>(settings);

                        await _SettingsRepository.FirstAdminRegistered(policies, _Options.UpdateUrl != null, _Options.DisableRegistration);
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

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(string userId, string code)
        {
            if (userId == null || code == null)
            {
                return RedirectToAction(nameof(HomeController.Index), "Home");
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
                return RedirectToAction("SetPassword", new { email = user.Email, code= await _userManager.GeneratePasswordResetTokenAsync(user)});
            }

            if (result.Succeeded)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Message = "Your email has been confirmed."
                });
                return RedirectToAction("Login", new {email = user.Email});
            }

            return View("Error");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
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
                    User = user, RequestUri = Request.GetAbsoluteRootUri()
                });
                return RedirectToAction(nameof(ForgotPasswordConfirmation));
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation()
        {
            return View();
        }

        [HttpGet]
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

            var model = new SetPasswordViewModel {Code = code, Email = email, EmailSetInternally = !string.IsNullOrEmpty(email)};
            return View(model);
        }

        [HttpPost]
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
                    Severity = StatusMessageModel.StatusSeverity.Success, Message = "Password successfully set."
                });
                return RedirectToAction(nameof(Login));
            }

            AddErrors(result);
            return View(model);
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
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
            else
            {
                return RedirectToAction(nameof(HomeController.Index), "Home");
            }
        }


        private bool CanLoginOrRegister()
        {
            return _btcPayServerEnvironment.IsDeveloping || _btcPayServerEnvironment.IsSecure;
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
