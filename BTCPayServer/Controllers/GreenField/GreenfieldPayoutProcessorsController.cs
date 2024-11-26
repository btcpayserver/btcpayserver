#nullable enable
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.PayoutProcessors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using PayoutProcessorData = BTCPayServer.Client.Models.PayoutProcessorData;

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
                PayoutMethods = factory.GetSupportedPayoutMethods().Select(id => id.ToString())
                    .ToArray()
            }));
        }
    }


}
