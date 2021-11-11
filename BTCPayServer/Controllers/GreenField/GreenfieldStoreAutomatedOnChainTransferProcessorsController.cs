#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.TransferProcessors;
using BTCPayServer.TransferProcessors.OnChain;
using BTCPayServer.TransferProcessors.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransferProcessorData = BTCPayServer.TransferProcessors.TransferProcessorData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class GreenfieldStoreAutomatedOnChainTransferProcessorsController : ControllerBase
    {
        private readonly TransferProcessorService _transferProcessorService;
        private readonly EventAggregator _eventAggregator;

        public GreenfieldStoreAutomatedOnChainTransferProcessorsController(TransferProcessorService transferProcessorService,
            EventAggregator eventAggregator)
        {
            _transferProcessorService = transferProcessorService;
            _eventAggregator = eventAggregator;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/transfer-processors/" + nameof(OnChainAutomatedTransferSenderFactory))]
        [HttpGet("~/api/v1/stores/{storeId}/transfer-processors/" + nameof(OnChainAutomatedTransferSenderFactory) +
                 "/{paymentMethod}")]
        public async Task<IActionResult> GetStoreOnChainAutomatedTransferProcessors(
            string storeId, string? paymentMethod)
        {
            var configured =
                await _transferProcessorService.GetProcessors(
                    new TransferProcessorService.TransferProcessorQuery()
                    {
                        Stores = new[] {storeId},
                        Processors = new[] {OnChainAutomatedTransferSenderFactory.ProcessorName},
                        PaymentMethods = paymentMethod is null ? null : new[] {paymentMethod}
                    });

            return Ok(configured.Select(ToModel).ToArray());
        }

        private static OnChainAutomatedTransferSettings ToModel(TransferProcessors.TransferProcessorData data)
        {
            return new OnChainAutomatedTransferSettings()
            {
                PaymentMethod = data.PaymentMethod,
                IntervalSeconds = InvoiceRepository.FromBytes<AutomatedTransferBlob>(data.Blob).Interval
            };
        }

        private static AutomatedTransferBlob FromModel(OnChainAutomatedTransferSettings data)
        {
            return new AutomatedTransferBlob() {Interval = data.IntervalSeconds};
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/transfer-processors/" + nameof(OnChainAutomatedTransferSenderFactory) +
                 "/{paymentMethod}")]
        public async Task<IActionResult> UpdateStoreOnchainAutomatedTransferProcessor(
            string storeId, string paymentMethod, OnChainAutomatedTransferSettings request)
        {
            var activeProcessor =
                (await _transferProcessorService.GetProcessors(
                    new TransferProcessorService.TransferProcessorQuery()
                    {
                        Stores = new[] {storeId},
                        Processors = new[] {OnChainAutomatedTransferSenderFactory.ProcessorName},
                        PaymentMethods = new[] {paymentMethod}
                    }))
                .FirstOrDefault();
            activeProcessor ??= new TransferProcessorData();
            activeProcessor.Blob = InvoiceRepository.ToBytes(FromModel(request));
            activeProcessor.StoreId = storeId;
            activeProcessor.PaymentMethod = paymentMethod;
            activeProcessor.Processor = OnChainAutomatedTransferSenderFactory.ProcessorName;
            var tcs = new TaskCompletionSource();
            _eventAggregator.Publish(new TransferProcessorUpdated()
            {
                Data = activeProcessor, Id = activeProcessor.Id, Processed = tcs
            });
            await tcs.Task;
            return Ok(ToModel(activeProcessor));
        }
    }

}
