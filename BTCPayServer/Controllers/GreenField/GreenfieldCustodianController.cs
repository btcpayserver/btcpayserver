using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Services.Custodian;
using BTCPayServer.Services.Custodian.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

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
            var result = new CustodianData();
            result.code = custodian.getCode();;
            result.name = custodian.getName();
            result.tradableAssetPairs = custodian.getTradableAssetPairs().Result;
            return result;
        }

    }
}
