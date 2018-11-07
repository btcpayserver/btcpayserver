using BTCPayServer.Models;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Controllers
{
    [ApiController]
    [Route("api/v1.0/[controller]")]
    [Authorize()]
    public class TestController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public TestController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [HttpGet("")]
        public IActionResult TestApi()
        {
            var x = _userManager.GetUserId(User);
            return Ok(x);
        }

        [HttpGet("{storeId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings.Key)]
        public IActionResult TestStoreApi(string storeId)
        {
            var x = _userManager.GetUserId(User);
            return Ok(x);
        }
    }
}
