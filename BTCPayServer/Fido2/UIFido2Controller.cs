using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Fido2.Models;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Fido2
{
    [Route("fido2")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
    public class UIFido2Controller(
        Fido2Service fido2Service,
        IStringLocalizer stringLocalizer) : Controller
    {
        private IStringLocalizer StringLocalizer { get; } = stringLocalizer;

        [HttpGet("{id}/delete")]
        public IActionResult Remove(string id, bool isPasskey = false)
        {
            return View("Confirm", new ConfirmModel(
                isPasskey ? StringLocalizer["Remove passkey"] : StringLocalizer["Remove security device"],
                isPasskey ? StringLocalizer["Your account will no longer have this passkey as an option for passwordless login."] : StringLocalizer["Your account will no longer have this security device as an option for two-factor authentication."],
                StringLocalizer["Delete"]));
        }

        [HttpPost("{id}/delete")]
        public async Task<IActionResult> RemoveP(string id, bool isPasskey = false)
        {
            await fido2Service.Remove(id, User.GetId());

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Html = (isPasskey ? StringLocalizer["The passkey was removed successfully."] : StringLocalizer["The security device was removed successfully."]).Value
            });

            return RedirectToList(isPasskey);
        }

        [HttpGet("register")]
        public async Task<IActionResult> Create(AddFido2CredentialViewModel viewModel)
        {
            var options = await fido2Service.RequestCreation(User.GetId(), viewModel.IsPasskey ? Fido2Credential.CredentialType.Passkey : Fido2Credential.CredentialType.FIDO2);
            if (options is null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = StringLocalizer["The security device could not be registered."].Value
                });

                return RedirectToList(viewModel.IsPasskey);
            }
            HttpContext.Session.SetString("FIDO", options.ToJson());

            ViewData["CredentialName"] = viewModel.Name ?? "";
            return View((options, viewModel.IsPasskey));
        }

        [HttpPost("register")]
        public async Task<IActionResult> CreateResponse([FromForm] string data, [FromForm] string name, [FromForm] bool isPasskey)
        {
            var options = CredentialCreateOptions.FromJson(HttpContext.Session.GetString("FIDO") ?? "");
            try
            {
                await fido2Service.CompleteCreation(User.GetId(), name, data, options,
                    isPasskey ? Fido2Credential.CredentialType.Passkey : Fido2Credential.CredentialType.FIDO2);
                HttpContext.Session.Remove("FIDO");
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Html = StringLocalizer["The security device was registered successfully."].Value
                });
            }
            catch (Exception ex)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = ex switch
                    {
                        Fido2VerificationException => StringLocalizer["The security device could not be registered. ({0})", ex.Message].Value,
                        _ => StringLocalizer["An unexpected error occurred while registering the security device."].Value
                    }
                });
            }

            return RedirectToList(isPasskey);
        }

        private ActionResult RedirectToList(bool isPasskey = false)
        {
            return RedirectToAction(isPasskey ? "Passkeys" : "TwoFactorAuthentication", "UIManage");
        }
    }
}
