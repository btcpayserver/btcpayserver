using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.TransferProcessors.OnChain;
using BTCPayServer.TransferProcessors.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.TransferProcessors.Lightning;

public class UILightningAutomatedTransferProcessorsController : Controller
{
    private readonly EventAggregator _eventAggregator;
    private readonly LightningAutomatedTransferSenderFactory _lightningAutomatedTransferSenderFactory;
    private readonly TransferProcessorService _transferProcessorService;

    public UILightningAutomatedTransferProcessorsController(
        EventAggregator eventAggregator,
        LightningAutomatedTransferSenderFactory lightningAutomatedTransferSenderFactory,
        TransferProcessorService transferProcessorService)
    {
        _eventAggregator = eventAggregator;
        _lightningAutomatedTransferSenderFactory = lightningAutomatedTransferSenderFactory;
        _transferProcessorService = transferProcessorService;
    }
    [HttpGet("~/stores/{storeId}/transfer-processors/lightning-automated/{cryptocode}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, string cryptoCode)
    {
        if (!_lightningAutomatedTransferSenderFactory.GetSupportedPaymentMethods().Any(id =>
                id.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase)))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = $"This processor cannot handle {cryptoCode}."
            });
            return RedirectToAction("ConfigureStoreTransferProcessors", "UITransferProcessors");
        }
        var activeProcessor =
            (await _transferProcessorService.GetProcessors(
                new TransferProcessorService.TransferProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new []{ _lightningAutomatedTransferSenderFactory.Processor},
                    PaymentMethods = new[]
                    {
                        new PaymentMethodId(cryptoCode, LightningPaymentType.Instance).ToString()
                    }
                }))
            .FirstOrDefault();

        return View (new LightningTransferViewModel(activeProcessor is null? new AutomatedTransferBlob() : OnChainTransferSender.GetBlob(activeProcessor)));
    }
    
    [HttpPost("~/stores/{storeId}/transfer-processors/lightning-automated/{cryptocode}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, string cryptoCode, LightningTransferViewModel automatedTransferBlob)
    {
        if (!_lightningAutomatedTransferSenderFactory.GetSupportedPaymentMethods().Any(id =>
                id.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase)))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = $"This processor cannot handle {cryptoCode}."
            });
            return RedirectToAction("ConfigureStoreTransferProcessors", "UITransferProcessors");
        }
        var activeProcessor =
            (await _transferProcessorService.GetProcessors(
                new TransferProcessorService.TransferProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new []{ _lightningAutomatedTransferSenderFactory.Processor},
                    PaymentMethods = new[]
                    {
                        new PaymentMethodId(cryptoCode, LightningPaymentType.Instance).ToString()
                    }
                }))
            .FirstOrDefault();
        activeProcessor ??= new TransferProcessorData();
        activeProcessor.Blob = InvoiceRepository.ToBytes(automatedTransferBlob.ToBlob());
        activeProcessor.StoreId = storeId;
        activeProcessor.PaymentMethod = new PaymentMethodId(cryptoCode, LightningPaymentType.Instance).ToString();
        activeProcessor.Processor = _lightningAutomatedTransferSenderFactory.Processor;
        var tcs = new TaskCompletionSource();
        _eventAggregator.Publish(new TransferProcessorUpdated()
        {
            Data = activeProcessor,
            Id = activeProcessor.Id,
            Processed = tcs
        });
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = $"Processor updated."
        });
        await tcs.Task;
        return RedirectToAction("ConfigureStoreTransferProcessors", "UITransferProcessors", new {storeId});
    }

    public class LightningTransferViewModel
    {
        public LightningTransferViewModel()
        {
            
        }

        public LightningTransferViewModel(AutomatedTransferBlob blob)
        {
            IntervalMinutes = blob.Interval.TotalMinutes;
        }
        public double IntervalMinutes { get; set; }

        public AutomatedTransferBlob ToBlob()
        {
            return new AutomatedTransferBlob() { Interval = TimeSpan.FromMinutes(IntervalMinutes) };
        }
    }
}
