#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using PayoutProcessorData = BTCPayServer.Client.Models.PayoutProcessorData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldPayoutProcessorsController : ControllerBase
    {
        private readonly IEnumerable<IPayoutProcessorFactory> _factories;

        public GreenfieldPayoutProcessorsController(IEnumerable<IPayoutProcessorFactory> factories)
        {
            _factories = factories;
        }

        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/payout-processors")]
        public IActionResult GetPayoutProcessors()
        {
            return Ok(_factories.Select(factory => new PayoutProcessorData()
            {
                Name = factory.Processor,
                FriendlyName = factory.FriendlyName,
                PaymentMethods = factory.GetSupportedPaymentMethods().Select(id => id.ToStringNormalized())
                    .ToArray()
            }));
        }
    }


}
