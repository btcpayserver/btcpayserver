#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.TransferProcessors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;
using TransferProcessorData = BTCPayServer.Client.Models.TransferProcessorData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class GreenfieldTransferProcessorsController : ControllerBase
    {
        private readonly IEnumerable<ITransferProcessorFactory> _factories;

        public GreenfieldTransferProcessorsController(IEnumerable<ITransferProcessorFactory>factories)
        {
            _factories = factories;
        }

        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/transfer-processors")]
        public IActionResult GetTransferProcessors()
        {
            return Ok(_factories.Select(factory => new TransferProcessorData()
            {
                Name = factory.Processor,
                FriendlyName = factory.FriendlyName,
                PaymentMethods = factory.GetSupportedPaymentMethods().Select(id => id.ToStringNormalized())
                    .ToArray()
            }));
        }
    }

    
}
