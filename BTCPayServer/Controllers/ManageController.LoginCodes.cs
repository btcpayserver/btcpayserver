using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class ManageController
    {
        [HttpGet]
        public async Task<IActionResult> RegenerateLoginCode()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            _userLoginCodeService.GetOrGenerate(user.Id, true);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Login code regenerated."
            });
            return RedirectToAction(nameof(ManageController.TwoFactorAuthentication));
        }
    }
}
