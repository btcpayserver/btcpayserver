using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services.Custodian;
using BTCPayServer.Services.Custodian.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using CustodianAccountData = BTCPayServer.Data.CustodianAccountData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldAPIKeys)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldCustodianController : ControllerBase
    {
        private readonly CustodianRegistry _custodianRegistry;

        public GreenfieldCustodianController(CustodianRegistry custodianRegistry)
        {
            _custodianRegistry = custodianRegistry;
        }

        [HttpGet("~/api/v1/custodian")]
        [Authorize(Policy = Policies.Unrestricted, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ListCustodians()
        {
            var all = _custodianRegistry.getAll().Values.ToList().Select(ToModel);
            return Ok(all);
        }

        private CustodianData ToModel(ICustodian custodian)
        {
            return new CustodianData(custodian);
        }

    }
}
