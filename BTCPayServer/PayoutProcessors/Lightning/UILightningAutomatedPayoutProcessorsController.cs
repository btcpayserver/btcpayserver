using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data.Data;
using BTCPayServer.Payments;
using BTCPayServer.PayoutProcessors.OnChain;
using BTCPayServer.PayoutProcessors.Settings;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.PayoutProcessors.Lightning;

public class UILightningAutomatedPayoutProcessorsController : Controller
{
    private readonly EventAggregator _eventAggregator;
    private readonly LightningAutomatedPayoutSenderFactory _lightningAutomatedPayoutSenderFactory;
    private readonly PayoutProcessorService _payoutProcessorService;

    public UILightningAutomatedPayoutProcessorsController(
        EventAggregator eventAggregator,
        LightningAutomatedPayoutSenderFactory lightningAutomatedPayoutSenderFactory,
        PayoutProcessorService payoutProcessorService)
    {
        _eventAggregator = eventAggregator;
        _lightningAutomatedPayoutSenderFactory = lightningAutomatedPayoutSenderFactory;
        _payoutProcessorService = payoutProcessorService;
    }
    [HttpGet("~/stores/{storeId}/payout-processors/lightning-automated/{cryptocode}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, string cryptoCode)
    {
        if (!_lightningAutomatedPayoutSenderFactory.GetSupportedPaymentMethods().Any(id =>
                id.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase)))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = $"This processor cannot handle {cryptoCode}."
            });
            return RedirectToAction("ConfigureStorePayoutProcessors", "UiPayoutProcessors");
        }
        var activeProcessor =
            (await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new []{ _lightningAutomatedPayoutSenderFactory.Processor},
                    PaymentMethods = new[]
                    {
                        new PaymentMethodId(cryptoCode, LightningPaymentType.Instance).ToString()
                    }
                }))
            .FirstOrDefault();

        return View (new LightningTransferViewModel(activeProcessor is null? new AutomatedPayoutBlob() : OnChainAutomatedPayoutProcessor.GetBlob(activeProcessor)));
    }
    
    [HttpPost("~/stores/{storeId}/payout-processors/lightning-automated/{cryptocode}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, string cryptoCode, LightningTransferViewModel automatedTransferBlob)
    {
        if (!_lightningAutomatedPayoutSenderFactory.GetSupportedPaymentMethods().Any(id =>
                id.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase)))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = $"This processor cannot handle {cryptoCode}."
            });
            return RedirectToAction("ConfigureStorePayoutProcessors", "UiPayoutProcessors");
        }
        var activeProcessor =
            (await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new []{ _lightningAutomatedPayoutSenderFactory.Processor},
                    PaymentMethods = new[]
                    {
                        new PaymentMethodId(cryptoCode, LightningPaymentType.Instance).ToString()
                    }
                }))
            .FirstOrDefault();
        activeProcessor ??= new PayoutProcessorData();
        activeProcessor.Blob = InvoiceRepository.ToBytes(automatedTransferBlob.ToBlob());
        activeProcessor.StoreId = storeId;
        activeProcessor.PaymentMethod = new PaymentMethodId(cryptoCode, LightningPaymentType.Instance).ToString();
        activeProcessor.Processor = _lightningAutomatedPayoutSenderFactory.Processor;
        var tcs = new TaskCompletionSource();
        _eventAggregator.Publish(new PayoutProcessorUpdated()
        {
            Data = activeProcessor,
            Id = activeProcessor.Id,
            Processed = tcs
        });
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = "Processor updated."
        });
        await tcs.Task;
        return RedirectToAction("ConfigureStorePayoutProcessors", "UiPayoutProcessors", new {storeId});
    }

    public class LightningTransferViewModel
    {
        public LightningTransferViewModel()
        {
            
        }

        public LightningTransferViewModel(AutomatedPayoutBlob blob)
        {
            IntervalMinutes = blob.Interval.TotalMinutes;
        }
        
        public double IntervalMinutes { get; set; }

        public AutomatedPayoutBlob ToBlob()
        {
            return new AutomatedPayoutBlob { Interval = TimeSpan.FromMinutes(IntervalMinutes) };
        }
    }
}
