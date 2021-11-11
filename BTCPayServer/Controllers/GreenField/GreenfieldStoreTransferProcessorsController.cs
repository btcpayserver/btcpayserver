#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.TransferProcessors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransferProcessorData = BTCPayServer.Client.Models.TransferProcessorData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class GreenfieldStoreTransferProcessorsController : ControllerBase
    {
        private readonly TransferProcessorService _transferProcessorService;
        private readonly IEnumerable<ITransferProcessorFactory> _factories;
        public GreenfieldStoreTransferProcessorsController(TransferProcessorService transferProcessorService, IEnumerable<ITransferProcessorFactory> factories)
        {
            _transferProcessorService = transferProcessorService;
            _factories = factories;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/transfer-processors")]
        public async Task<IActionResult> GetStoreTransferProcessors(
            string storeId)
        {
            var configured =
                (await _transferProcessorService.GetProcessors(
                    new TransferProcessorService.TransferProcessorQuery() { Stores = new[] { storeId } }))
                .GroupBy(data => data.Processor).Select(datas => new TransferProcessorData()
                {
                    Name = datas.Key,
                    FriendlyName = _factories.FirstOrDefault(factory => factory.Processor == datas.Key)?.FriendlyName,
                    PaymentMethods = datas.Select(data => data.PaymentMethod).ToArray()
                });
            return Ok(configured);

        }
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/transfer-processors/{processor}/{paymentMethod}")]
        public async Task<IActionResult> RemoveStoreTransferProcessor(
            string storeId,string processor,string paymentMethod)
        {
            var matched =
                (await _transferProcessorService.GetProcessors(
                    new TransferProcessorService.TransferProcessorQuery()
                    {
                        Stores = new[] { storeId },
                        Processors = new []{ processor},
                        PaymentMethods = new []{paymentMethod}
                    })).FirstOrDefault();
            if (matched is null)
            {
                return NotFound();
            }

            var tcs = new TaskCompletionSource();
            _transferProcessorService.EventAggregator.Publish(new TransferProcessorUpdated()
            {
                Id = matched.Id,
                Processed = tcs
            });
            await tcs.Task;
            return Ok();

        }
    }
}
