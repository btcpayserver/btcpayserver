using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace BTCPayServer.Controllers
{
    public partial class StoresController : Controller
    {

        [Authorize(AuthenticationSchemes = OpenIdConnectDefaults.AuthenticationScheme)]
        [HttpGet("test")]
        public IActionResult TestApi()
        {
            var x = _UserManager.GetUserId(User);
            return Ok(x);
        }
    }
}
