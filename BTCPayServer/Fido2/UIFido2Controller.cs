using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Fido2.Models;
using Microsoft.AspNetCore.Authorization;
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
        public IActionResult Remove(string id)
        {
            return View("Confirm", new ConfirmModel(StringLocalizer["Remove security device"], StringLocalizer["Your account will no longer have this security device as an option for two-factor authentication."], StringLocalizer["Delete"]));
        }

        [HttpPost("{id}/delete")]
        public async Task<IActionResult> RemoveP(string id)
        {
            await fido2Service.Remove(id, User.GetId());

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Html = StringLocalizer["The security device was removed successfully."].Value
            });

            return RedirectToList();
        }

        [HttpGet("register")]
        public async Task<IActionResult> Create(AddFido2CredentialViewModel viewModel)
        {
            var options = await fido2Service.RequestCreation(User.GetId());
            if (options is null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = StringLocalizer["The security device could not be registered."].Value
                });

                return RedirectToList();
            }

            ViewData["CredentialName"] = viewModel.Name ?? "";
            return View(options);
        }

        [HttpPost("register")]
        public async Task<IActionResult> CreateResponse([FromForm] string data, [FromForm] string name)
        {
            if (await fido2Service.CompleteCreation(User.GetId(), name, data))
            {

                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Html = StringLocalizer["The security device was registered successfully."].Value
                });
            }
            else
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = StringLocalizer["The security device could not be registered."].Value
                });
            }

            return RedirectToList();
        }

        private ActionResult RedirectToList()
        {
            return RedirectToAction("TwoFactorAuthentication", "UIManage");
        }
    }
}
