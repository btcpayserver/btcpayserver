using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.Greenfield
{
    [Controller]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldHealthController : ControllerBase
    {
        private readonly NBXplorerDashboard _dashBoard;

        public GreenfieldHealthController(NBXplorerDashboard dashBoard)
        {
            _dashBoard = dashBoard;
        }
        [AllowAnonymous]
        [HttpGet("~/api/v1/health")]
        public ActionResult GetHealth()
        {
            ApiHealthData model = new ApiHealthData()
            {
                Synchronized = _dashBoard.IsFullySynched()
            };
            return Ok(model);
        }
    }
}
