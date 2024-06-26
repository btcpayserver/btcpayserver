using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Fido2.Models;
using BTCPayServer.Models;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Fido2
{
    [Route("fido2")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
    public class UIFido2Controller : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly Fido2Service _fido2Service;

        public UIFido2Controller(UserManager<ApplicationUser> userManager, Fido2Service fido2Service)
        {
            _userManager = userManager;
            _fido2Service = fido2Service;
        }

        [HttpGet("{id}/delete")]
        public IActionResult Remove(string id)
        {
            return View("Confirm", new ConfirmModel("Remove security device", "Your account will no longer have this security device as an option for two-factor authentication.", "Remove"));
        }

        [HttpPost("{id}/delete")]
        public async Task<IActionResult> RemoveP(string id)
        {
            await _fido2Service.Remove(id, _userManager.GetUserId(User));

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Html = "The security device was removed successfully."
            });

            return RedirectToList();
        }

        [HttpGet("register")]
        public async Task<IActionResult> Create(AddFido2CredentialViewModel viewModel)
        {
            var options = await _fido2Service.RequestCreation(_userManager.GetUserId(User));
            if (options is null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = "The security device could not be registered."
                });

                return RedirectToList();
            }

            ViewData["CredentialName"] = viewModel.Name ?? "";
            return View(options);
        }

        [HttpPost("register")]
        public async Task<IActionResult> CreateResponse([FromForm] string data, [FromForm] string name)
        {
            if (await _fido2Service.CompleteCreation(_userManager.GetUserId(User), name, data))
            {

                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Html = "The security device was registered successfully."
                });
            }
            else
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Html = "The security device could not be registered."
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
