using System.Linq;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client.Models;
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

        [HttpGet("~/api/v1/custodians")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public IActionResult ListCustodians()
        {
            var all = _custodianRegistry.GetAll().Values.ToList().Select(ToModel);
            return Ok(all);
        }

        private CustodianData ToModel(ICustodian custodian)
        {
            var result = new CustodianData();
            result.Code = custodian.GetCode();;
            result.Name = custodian.GetName();

            if (custodian is ICanTrade tradableCustodian)
            {
                var tradableAssetPairs = tradableCustodian.GetTradableAssetPairs();
                var tradableAssetPairStrings = new string[tradableAssetPairs.Count];
                for (int i = 0; i < tradableAssetPairs.Count; i++)
                {
                    tradableAssetPairStrings[i] = tradableAssetPairs[i].ToString();
                }
                result.TradableAssetPairs = tradableAssetPairStrings;
            }

            if (custodian is ICanDeposit depositableCustodian)
            {
                result.DepositablePaymentMethods = depositableCustodian.GetDepositablePaymentMethods();
            }
            if (custodian is ICanWithdraw withdrawableCustodian)
            {
                result.WithdrawablePaymentMethods = withdrawableCustodian.GetWithdrawablePaymentMethods();
            }
            return result;
        }

    }
}
