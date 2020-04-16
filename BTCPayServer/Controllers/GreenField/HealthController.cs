using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    public class HealthController : ControllerBase
    {
        [AllowAnonymous]
        [HttpGet("~/api/v1/health")]
        public ActionResult GetHealth()
        {
            return Ok();
        }
    }
}
