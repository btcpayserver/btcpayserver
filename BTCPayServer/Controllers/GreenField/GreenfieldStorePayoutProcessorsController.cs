#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.PayoutProcessors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStorePayoutProcessorsController : ControllerBase
    {
        private readonly PayoutProcessorService _payoutProcessorService;
        private readonly IEnumerable<IPayoutProcessorFactory> _factories;
        public GreenfieldStorePayoutProcessorsController(PayoutProcessorService payoutProcessorService, IEnumerable<IPayoutProcessorFactory> factories)
        {
            _payoutProcessorService = payoutProcessorService;
            _factories = factories;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payout-processors")]
        public async Task<IActionResult> GetStorePayoutProcessors(
            string storeId)
        {
            var configured =
                (await _payoutProcessorService.GetProcessors(
                    new PayoutProcessorService.PayoutProcessorQuery() { Stores = new[] { storeId } }))
                .GroupBy(data => data.Processor).Select(datas => new PayoutProcessorData()
                {
                    Name = datas.Key,
                    FriendlyName = _factories.FirstOrDefault(factory => factory.Processor == datas.Key)?.FriendlyName,
                    PaymentMethods = datas.Select(data => data.PaymentMethod).ToArray()
                });
            return Ok(configured);

        }
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payout-processors/{processor}/{paymentMethod}")]
        public async Task<IActionResult> RemoveStorePayoutProcessor(
            string storeId, string processor, string paymentMethod)
        {
            var matched =
                (await _payoutProcessorService.GetProcessors(
                    new PayoutProcessorService.PayoutProcessorQuery()
                    {
                        Stores = new[] { storeId },
                        Processors = new[] { processor },
                        PaymentMethods = new[] { paymentMethod }
                    })).FirstOrDefault();
            if (matched is null)
            {
                return NotFound();
            }

            var tcs = new TaskCompletionSource();
            _payoutProcessorService.EventAggregator.Publish(new PayoutProcessorUpdated()
            {
                Id = matched.Id,
                Processed = tcs
            });
            await tcs.Task;
            return Ok();

        }
    }
}
