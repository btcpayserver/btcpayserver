using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.Greenfield
{
    [Controller]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldHealthController(NBXplorerDashboard dashBoard) : ControllerBase
    {
        [AllowAnonymous]
        [HttpGet("~/api/v1/health")]
        public ActionResult GetHealth()
        =>  Ok( new ApiHealthData()
        {
            Synchronized = dashBoard.IsFullySynched()
        });
    }
}
