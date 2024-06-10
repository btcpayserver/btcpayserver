using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class UIManageController
    {
        [HttpGet]
        public async Task<IActionResult> LoginCodes()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }
            
            var indexUrl = _linkGenerator.IndexLink(Request.Scheme, Request.Host, Request.PathBase);
            var loginCode = _userLoginCodeService.GetOrGenerate(user.Id);
            return View(nameof(LoginCodes), $"{loginCode};{indexUrl};{user.Email}");
        }
    }
}
