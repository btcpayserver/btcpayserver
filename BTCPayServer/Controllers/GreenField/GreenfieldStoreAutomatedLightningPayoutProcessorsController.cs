#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data.Data;
using BTCPayServer.Payments;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.PayoutProcessors.Lightning;
using BTCPayServer.PayoutProcessors.Settings;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayoutProcessorData = BTCPayServer.Data.Data.PayoutProcessorData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class GreenfieldStoreAutomatedLightningPayoutProcessorsController : ControllerBase
    {
        private readonly PayoutProcessorService _payoutProcessorService;
        private readonly EventAggregator _eventAggregator;

        public GreenfieldStoreAutomatedLightningPayoutProcessorsController(PayoutProcessorService payoutProcessorService,
            EventAggregator eventAggregator)
        {
            _payoutProcessorService = payoutProcessorService;
            _eventAggregator = eventAggregator;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payout-processors/" + nameof(LightningAutomatedPayoutSenderFactory))]
        [HttpGet("~/api/v1/stores/{storeId}/payout-processors/" + nameof(LightningAutomatedPayoutSenderFactory) +
                 "/{paymentMethod}")]
        public async Task<IActionResult> GetStoreLightningAutomatedPayoutProcessors(
            string storeId, string? paymentMethod)
        {
            paymentMethod = !string.IsNullOrEmpty(paymentMethod) ? PaymentMethodId.Parse(paymentMethod).ToString() : null;
            var configured =
                await _payoutProcessorService.GetProcessors(
                    new PayoutProcessorService.PayoutProcessorQuery()
                    {
                        Stores = new[] {storeId},
                        Processors = new[] {LightningAutomatedPayoutSenderFactory.ProcessorName},
                        PaymentMethods = paymentMethod is null ? null : new[] {paymentMethod}
                    });

            return Ok(configured.Select(ToModel).ToArray());
        }

        private static LightningAutomatedPayoutSettings ToModel(PayoutProcessorData data)
        {
            return new LightningAutomatedPayoutSettings()
            {
                PaymentMethod = data.PaymentMethod,
                IntervalSeconds = InvoiceRepository.FromBytes<AutomatedPayoutBlob>(data.Blob).Interval
            };
        }

        private static AutomatedPayoutBlob FromModel(LightningAutomatedPayoutSettings data)
        {
            return new AutomatedPayoutBlob() {Interval = data.IntervalSeconds};
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/payout-processors/" + nameof(LightningAutomatedPayoutSenderFactory) +
                 "/{paymentMethod}")]
        public async Task<IActionResult> UpdateStoreLightningAutomatedPayoutProcessor(
            string storeId, string paymentMethod, LightningAutomatedPayoutSettings request)
        {
            paymentMethod = PaymentMethodId.Parse(paymentMethod).ToString();
            var activeProcessor =
                (await _payoutProcessorService.GetProcessors(
                    new PayoutProcessorService.PayoutProcessorQuery()
                    {
                        Stores = new[] {storeId},
                        Processors = new[] {LightningAutomatedPayoutSenderFactory.ProcessorName},
                        PaymentMethods = new[] {paymentMethod}
                    }))
                .FirstOrDefault();
            activeProcessor ??= new PayoutProcessorData();
            activeProcessor.Blob = InvoiceRepository.ToBytes(FromModel(request));
            activeProcessor.StoreId = storeId;
            activeProcessor.PaymentMethod = paymentMethod;
            activeProcessor.Processor = LightningAutomatedPayoutSenderFactory.ProcessorName;
            var tcs = new TaskCompletionSource();
            _eventAggregator.Publish(new PayoutProcessorUpdated()
            {
                Data = activeProcessor, Id = activeProcessor.Id, Processed = tcs
            });
            await tcs.Task;
            return Ok(ToModel(activeProcessor));
        }
    }
}
