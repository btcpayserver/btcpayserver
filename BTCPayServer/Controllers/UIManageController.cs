using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Fido2;
using BTCPayServer.Models.ManageViewModels;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

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
        private readonly CallbackGenerator _callbackGenerator;
        private readonly IHtmlHelper Html;
        private readonly UserService _userService;
        private readonly UriResolver _uriResolver;
        private readonly IFileService _fileService;
        private readonly EventAggregator _eventAggregator;
        readonly StoreRepository _StoreRepository;
        public IStringLocalizer StringLocalizer { get; }

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
          CallbackGenerator callbackGenerator,
          UserService userService,
          UriResolver uriResolver,
          IFileService fileService,
          IStringLocalizer stringLocalizer,
          IHtmlHelper htmlHelper,
          EventAggregator eventAggregator)
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
            _callbackGenerator = callbackGenerator;
            Html = htmlHelper;
            _eventAggregator = eventAggregator;
            _userService = userService;
            _uriResolver = uriResolver;
            _fileService = fileService;
            _StoreRepository = storeRepository;
            StringLocalizer = stringLocalizer;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }
            var blob = user.GetBlob() ?? new();
            var model = new IndexViewModel
            {
                Email = user.Email,
                Name = blob.Name,
                ImageUrl = string.IsNullOrEmpty(blob.ImageUrl) ? null : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), UnresolvedUri.Create(blob.ImageUrl)),
                EmailConfirmed = user.EmailConfirmed,
                RequiresEmailConfirmation = user.RequiresEmailConfirmation
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

            var blob = user.GetBlob() ?? new();
            blob.ShowInvoiceStatusChangeHint = false;
            user.SetBlob(blob);
            await _userManager.UpdateAsync(user);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(IndexViewModel model, [FromForm] bool RemoveImageFile = false)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            bool needUpdate = false;
            var email = user.Email;
            if (model.Email != email)
            {
                if (!(await _userManager.FindByEmailAsync(model.Email) is null))
                {
                    TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["The email address is already in use with an other account."].Value;
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
                needUpdate = true;
            }

            var blob = user.GetBlob() ?? new();
            if (blob.Name != model.Name)
            {
                blob.Name = model.Name;
                needUpdate = true;
            }

            if (model.ImageFile != null)
            {
                var imageUpload = await _fileService.UploadImage(model.ImageFile, user.Id);
                if (!imageUpload.Success)
                    ModelState.AddModelError(nameof(model.ImageFile), imageUpload.Response);
                else
                {
                    try
                    {
                        var storedFile = imageUpload.StoredFile!;
                        var fileIdUri = new UnresolvedUri.FileIdUri(storedFile.Id);
                        blob.ImageUrl = fileIdUri.ToString();
                        needUpdate = true;
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError(nameof(model.ImageFile), $"Could not save image: {e.Message}");
                    }
                }
            }
            else if (RemoveImageFile && !string.IsNullOrEmpty(blob.ImageUrl))
            {
                blob.ImageUrl = null;
                needUpdate = true;
            }
            user.SetBlob(blob);
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (needUpdate && await _userManager.UpdateAsync(user) is { Succeeded: true })
            {
                _eventAggregator.Publish(new UserEvent.Updated(user));
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Your profile has been updated"].Value;
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Error updating profile"].Value;
            }

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

            var callbackUrl = await _callbackGenerator.ForEmailConfirmation(user);
            _eventAggregator.Publish(new UserEvent.ConfirmationEmailRequested(user, callbackUrl));
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Verification email sent. Please check your email."].Value;
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
            _logger.LogInformation("User changed their password successfully");
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Your password has been changed."].Value;

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
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Your password has been set."].Value;

            return RedirectToAction(nameof(SetPassword));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUserPost()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            await _userService.DeleteUserAndAssociatedData(user);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Account successfully deleted."].Value;
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
