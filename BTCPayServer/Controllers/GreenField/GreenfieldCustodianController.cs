using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
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
        public IActionResult ListCustodians()
        {
            var all = _custodianRegistry.getAll().Values.ToList().Select(ToModel);
            return Ok(all);
        }

        private CustodianData ToModel(ICustodian custodian)
        {
            var result = new CustodianData();
            result.code = custodian.GetCode();;
            result.name = custodian.GetName();

            var tradableAssetPairs = custodian.GetTradableAssetPairs();
            var tradableAssetPairStrings = new string[tradableAssetPairs.Count];
            for (int i = 0; i< tradableAssetPairs.Count; i++)
            {
                tradableAssetPairStrings[i] = tradableAssetPairs[i].ToString();
            }
            result.tradableAssetPairs = tradableAssetPairStrings;
            
            if (custodian is ICanDeposit depositableCustodian)
            {
                // TODO complete this
                // result.depositablePaymentMethods = new string[] {};
            }
            if (custodian is ICanWithdraw withdrawableCustodian)
            {
                // TODO complete this
                // result.withdrawablePaymentMethods = new string[] {};
            }
            return result;
        }

    }
}
