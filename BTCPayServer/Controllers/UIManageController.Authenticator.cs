using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Models.ManageViewModels;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class UIManageController
    {
        private const string AuthenicatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

        [HttpGet]
        public async Task<IActionResult> EnableAuthenticator()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound();

            var model = new EnableAuthenticatorViewModel();
            await LoadSharedKeyAndQrCodeUriAsync(user, model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnableAuthenticator(EnableAuthenticatorViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound();

            if (!ModelState.IsValid)
            {
                await LoadSharedKeyAndQrCodeUriAsync(user, model);
                return View(model);
            }

            // Strip spaces and hypens
            var verificationCode = model.Code.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);

            var is2faTokenValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

            if (!is2faTokenValid)
            {
                ModelState.AddModelError("Code", "Verification code is invalid.");
                await LoadSharedKeyAndQrCodeUriAsync(user, model);
                return View(model);
            }

            user.AuthenticatorEnabled = true;
            await _userManager.UpdateAsync(user);

            TempData.SetStatusSuccess(StringLocalizer["Authenticator enabled successfully."]);
            return RedirectToAction(nameof(TwoFactorAuthentication));
        }

        [HttpPost]
        public async Task<IActionResult> ResetAuthenticator()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound();
            await _userManager.ResetAuthenticatorKeyAsync(user);
            user.AuthenticatorEnabled = false;
            await _userManager.UpdateAsync(user);
            TempData.SetStatusSuccess(StringLocalizer["Authenticator disabled successfully."]);
            return RedirectToAction(nameof(EnableAuthenticator));
        }

        private string GenerateQrCodeUri(string email, string unformattedKey)
        {
            return string.Format(CultureInfo.InvariantCulture,
                AuthenicatorUriFormat,
                _urlEncoder.Encode("BTCPayServer"),
                _urlEncoder.Encode(email),
                unformattedKey);
        }

        private string FormatKey(string unformattedKey)
        {
            var result = new StringBuilder();
            int currentPosition = 0;
            while (currentPosition + 4 < unformattedKey.Length)
            {
                result.Append(unformattedKey.Substring(currentPosition, 4)).Append(' ');
                currentPosition += 4;
            }

            if (currentPosition < unformattedKey.Length)
            {
                result.Append(unformattedKey.Substring(currentPosition));
            }

            return result.ToString().ToLowerInvariant();
        }


        private async Task LoadSharedKeyAndQrCodeUriAsync(ApplicationUser user, EnableAuthenticatorViewModel model)
        {
            var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(unformattedKey))
            {
                await _userManager.ResetAuthenticatorKeyAsync(user);
                unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            }

            model.SharedKey = FormatKey(unformattedKey);
            model.AuthenticatorUri = GenerateQrCodeUri(user.Email, unformattedKey);
        }

        [HttpPost]
        public IActionResult CreateCredential(string name, Fido2Credential.CredentialType type)
        {
            switch (type)
            {
                case Fido2Credential.CredentialType.FIDO2:
                    return RedirectToAction("Create", "UIFido2", new { name });
                case Fido2Credential.CredentialType.Passkey:
                    return RedirectToAction("Create", "UIFido2", new { name, isPasskey = true });
                case Fido2Credential.CredentialType.LNURLAuth:
                    return RedirectToAction("Create", "UILNURLAuth", new { name });
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}
