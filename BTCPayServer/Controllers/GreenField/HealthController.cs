using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [Controller]
    [EnableCors(CorsPolicies.All)]
    public class HealthController : ControllerBase
    {
        [AllowAnonymous]
        [HttpGet("~/api/v1/health")]
        public ActionResult GetHealth(NBXplorerDashboard dashBoard)
        {
            ApiHealthData model = new ApiHealthData()
            {
                Synchronized = dashBoard.IsFullySynched()
            };
            return Ok(model);
        }
    }
}
