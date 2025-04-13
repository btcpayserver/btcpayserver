#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.PayoutProcessors.OnChain;
using BTCPayServer.Payouts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using PayoutProcessorData = BTCPayServer.Data.PayoutProcessorData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreAutomatedOnChainPayoutProcessorsController : ControllerBase
    {
        private readonly PayoutProcessorService _payoutProcessorService;
        private readonly EventAggregator _eventAggregator;

        public GreenfieldStoreAutomatedOnChainPayoutProcessorsController(PayoutProcessorService payoutProcessorService,
            EventAggregator eventAggregator)
        {
            _payoutProcessorService = payoutProcessorService;
            _eventAggregator = eventAggregator;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payout-processors/OnChainAutomatedPayoutSenderFactory")]
        [HttpGet("~/api/v1/stores/{storeId}/payout-processors/OnChainAutomatedPayoutSenderFactory/{paymentMethod}")]
        public async Task<IActionResult> GetStoreOnChainAutomatedPayoutProcessors(
            string storeId, string? paymentMethod)
        {
            var paymentMethodId = !string.IsNullOrEmpty(paymentMethod) ? PayoutMethodId.Parse(paymentMethod) : null;
            var configured =
                await _payoutProcessorService.GetProcessors(
                    new PayoutProcessorService.PayoutProcessorQuery()
                    {
                        Stores = new[] { storeId },
                        Processors = new[] { OnChainAutomatedPayoutSenderFactory.ProcessorName },
                        PayoutMethods = paymentMethodId is null ? null : new[] { paymentMethodId }
                    });

            return Ok(configured.Select(ToModel).ToArray());
        }

        private static OnChainAutomatedPayoutSettings ToModel(PayoutProcessorData data)
        {
            var blob = BaseAutomatedPayoutProcessor<OnChainAutomatedPayoutBlob>.GetBlob(data);
            return new OnChainAutomatedPayoutSettings()
            {
                FeeBlockTarget = blob.FeeTargetBlock,
                PayoutMethodId = data.PayoutMethodId,
                IntervalSeconds = blob.Interval,
                Threshold = blob.Threshold,
                ProcessNewPayoutsInstantly = blob.ProcessNewPayoutsInstantly
            };
        }

        private static OnChainAutomatedPayoutBlob FromModel(OnChainAutomatedPayoutSettings data)
        {
            return new OnChainAutomatedPayoutBlob()
            {
                FeeTargetBlock = data.FeeBlockTarget ?? 1,
                Interval = data.IntervalSeconds,
                Threshold = data.Threshold,
                ProcessNewPayoutsInstantly = data.ProcessNewPayoutsInstantly
            };
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/payout-processors/OnChainAutomatedPayoutSenderFactory/{paymentMethod}")]
        public async Task<IActionResult> UpdateStoreOnchainAutomatedPayoutProcessor(
            string storeId, string paymentMethod, OnChainAutomatedPayoutSettings request)
        {
            AutomatedPayoutConstants.ValidateInterval(ModelState, request.IntervalSeconds, nameof(request.IntervalSeconds));
            if (request.FeeBlockTarget is int t && (t < 1 || t > 1000))
                ModelState.AddModelError(nameof(request.FeeBlockTarget), "The feeBlockTarget should be between 1 and 1000");
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            var payoutMethodId = PayoutMethodId.Parse(paymentMethod);
            var activeProcessor =
                (await _payoutProcessorService.GetProcessors(
                    new PayoutProcessorService.PayoutProcessorQuery()
                    {
                        Stores = new[] { storeId },
                        Processors = new[] { OnChainAutomatedPayoutSenderFactory.ProcessorName },
                        PayoutMethods = new[] { payoutMethodId }
                    }))
                .FirstOrDefault();
            activeProcessor ??= new PayoutProcessorData();
            activeProcessor.HasTypedBlob<OnChainAutomatedPayoutBlob>().SetBlob(FromModel(request));
            activeProcessor.StoreId = storeId;
            activeProcessor.PayoutMethodId = payoutMethodId.ToString();
            activeProcessor.Processor = OnChainAutomatedPayoutSenderFactory.ProcessorName;
            var tcs = new TaskCompletionSource();
            _eventAggregator.Publish(new PayoutProcessorUpdated()
            {
                Data = activeProcessor,
                Id = activeProcessor.Id,
                Processed = tcs
            });
            await tcs.Task;
            return Ok(ToModel(activeProcessor));
        }
    }

}
