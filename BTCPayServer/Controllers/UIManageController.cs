using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Fido2;
using BTCPayServer.Models;
using BTCPayServer.Models.ManageViewModels;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.Controllers
{

    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
    [Route("account/{action:lowercase=Index}")]
    public partial class UIManageController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly EmailSenderFactory _EmailSenderFactory;
        private readonly ILogger _logger;
        private readonly UrlEncoder _urlEncoder;
        private readonly BTCPayServerEnvironment _btcPayServerEnvironment;
        private readonly APIKeyRepository _apiKeyRepository;
        private readonly IAuthorizationService _authorizationService;
        private readonly Fido2Service _fido2Service;
        private readonly LinkGenerator _linkGenerator;
        private readonly UserLoginCodeService _userLoginCodeService;
        private readonly IHtmlHelper Html;
        private readonly UserService _userService;
        readonly StoreRepository _StoreRepository;

        public UIManageController(
          UserManager<ApplicationUser> userManager,
          SignInManager<ApplicationUser> signInManager,
          EmailSenderFactory emailSenderFactory,
          ILogger<UIManageController> logger,
          UrlEncoder urlEncoder,
          StoreRepository storeRepository,
          BTCPayServerEnvironment btcPayServerEnvironment,
          APIKeyRepository apiKeyRepository,
          IAuthorizationService authorizationService,
          Fido2Service fido2Service,
          LinkGenerator linkGenerator,
          UserService userService,
          UserLoginCodeService userLoginCodeService,
          IHtmlHelper htmlHelper
          )
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _EmailSenderFactory = emailSenderFactory;
            _logger = logger;
            _urlEncoder = urlEncoder;
            _btcPayServerEnvironment = btcPayServerEnvironment;
            _apiKeyRepository = apiKeyRepository;
            _authorizationService = authorizationService;
            _fido2Service = fido2Service;
            _linkGenerator = linkGenerator;
            _userLoginCodeService = userLoginCodeService;
            Html = htmlHelper;
            _userService = userService;
            _StoreRepository = storeRepository;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var model = new IndexViewModel
            {
                Username = user.UserName,
                Email = user.Email,
                IsEmailConfirmed = user.EmailConfirmed
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableShowInvoiceStatusChangeHint()
        {

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var blob = user.GetBlob();
            blob.ShowInvoiceStatusChangeHint = false;
            user.SetBlob(blob);
            await _userManager.UpdateAsync(user);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(IndexViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var email = user.Email;
            if (model.Email != email)
            {
                if (!(await _userManager.FindByEmailAsync(model.Email) is null))
                {
                    TempData[WellKnownTempData.ErrorMessage] = "The email address is already in use with an other account.";
                    return RedirectToAction(nameof(Index));
                }
                var setUserResult = await _userManager.SetUserNameAsync(user, model.Email);
                if (!setUserResult.Succeeded)
                {
                    throw new ApplicationException($"Unexpected error occurred setting email for user with ID '{user.Id}'.");
                }
                var setEmailResult = await _userManager.SetEmailAsync(user, model.Email);
                if (!setEmailResult.Succeeded)
                {
                    throw new ApplicationException($"Unexpected error occurred setting email for user with ID '{user.Id}'.");
                }
            }
            TempData[WellKnownTempData.SuccessMessage] = "Your profile has been updated";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendVerificationEmail(IndexViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(nameof(Index), model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var callbackUrl = _linkGenerator.EmailConfirmationLink(user.Id, code, Request.Scheme, Request.Host, Request.PathBase);
            (await _EmailSenderFactory.GetEmailSender()).SendEmailConfirmation(user.GetMailboxAddress(), callbackUrl);
            TempData[WellKnownTempData.SuccessMessage] = "Verification email sent. Please check your email.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ChangePassword()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var hasPassword = await _userManager.HasPasswordAsync(user);
            if (!hasPassword)
            {
                return RedirectToAction(nameof(SetPassword));
            }

            var model = new ChangePasswordViewModel();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var changePasswordResult = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
            if (!changePasswordResult.Succeeded)
            {
                AddErrors(changePasswordResult);
                return View(model);
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            _logger.LogInformation("User changed their password successfully.");
            TempData[WellKnownTempData.SuccessMessage] = "Your password has been changed.";

            return RedirectToAction(nameof(ChangePassword));
        }

        [HttpGet]
        public async Task<IActionResult> SetPassword()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var hasPassword = await _userManager.HasPasswordAsync(user);

            if (hasPassword)
            {
                return RedirectToAction(nameof(ChangePassword));
            }

            var model = new SetPasswordViewModel();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var addPasswordResult = await _userManager.AddPasswordAsync(user, model.NewPassword);
            if (!addPasswordResult.Succeeded)
            {
                AddErrors(addPasswordResult);
                return View(model);
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            TempData[WellKnownTempData.SuccessMessage] = "Your password has been set.";

            return RedirectToAction(nameof(SetPassword));
        }

        [HttpPost()]
        public async Task<IActionResult> DeleteUserPost()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            await _userService.DeleteUserAndAssociatedData(user);
            TempData[WellKnownTempData.SuccessMessage] = "Account successfully deleted.";
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(UIAccountController.Login), "UIAccount");
        }


        #region Helpers

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
        #endregion
    }
}
