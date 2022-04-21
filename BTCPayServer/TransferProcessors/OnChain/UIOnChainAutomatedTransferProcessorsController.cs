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
using BTCPayServer.TransferProcessors.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.TransferProcessors.OnChain;

public class UIOnChainAutomatedTransferProcessorsController : Controller
{
    private readonly EventAggregator _eventAggregator;
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly OnChainAutomatedTransferSenderFactory _onChainAutomatedTransferSenderFactory;
    private readonly TransferProcessorService _transferProcessorService;

    public UIOnChainAutomatedTransferProcessorsController(
        EventAggregator eventAggregator,
        BTCPayNetworkProvider btcPayNetworkProvider,
        OnChainAutomatedTransferSenderFactory onChainAutomatedTransferSenderFactory,
        TransferProcessorService transferProcessorService)
    {
        _eventAggregator = eventAggregator;
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _onChainAutomatedTransferSenderFactory = onChainAutomatedTransferSenderFactory;
        _transferProcessorService = transferProcessorService;
        ;
    }

    
    [HttpGet("~/stores/{storeId}/transfer-processors/onchain-automated/{cryptocode}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, string cryptoCode)
    {
        if (!_onChainAutomatedTransferSenderFactory.GetSupportedPaymentMethods().Any(id =>
                id.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase)))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = $"This processor cannot handle {cryptoCode}."
            });
            return RedirectToAction("ConfigureStoreTransferProcessors", "UITransferProcessors");
        }
        var wallet = HttpContext.GetStoreData().GetDerivationSchemeSettings(_btcPayNetworkProvider, cryptoCode);
        if (wallet?.IsHotWallet is not true)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = $"Either your {cryptoCode} wallet is not configured, or it is not a hot wallet. This processor cannot function until a hot wallet is configured in your store."
            });
        }
        var activeProcessor =
            (await _transferProcessorService.GetProcessors(
                new TransferProcessorService.TransferProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new []{ _onChainAutomatedTransferSenderFactory.Processor},
                    PaymentMethods = new[]
                    {
                        new PaymentMethodId(cryptoCode, BitcoinPaymentType.Instance).ToString()
                    }
                }))
            .FirstOrDefault();

        return View (new OnChainTransferViewModel(activeProcessor is null? new AutomatedTransferBlob() : OnChainTransferSender.GetBlob(activeProcessor)));
    }
    
    [HttpPost("~/stores/{storeId}/transfer-processors/onchain-automated/{cryptocode}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, string cryptoCode, OnChainTransferViewModel automatedTransferBlob)
    {
        if (!_onChainAutomatedTransferSenderFactory.GetSupportedPaymentMethods().Any(id =>
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
                    Processors = new []{ OnChainAutomatedTransferSenderFactory.ProcessorName},
                    PaymentMethods = new[]
                    {
                        new PaymentMethodId(cryptoCode, BitcoinPaymentType.Instance).ToString()
                    }
                }))
            .FirstOrDefault();
        activeProcessor ??= new TransferProcessorData();
        activeProcessor.Blob = InvoiceRepository.ToBytes(automatedTransferBlob.ToBlob());
        activeProcessor.StoreId = storeId;
        activeProcessor.PaymentMethod = new PaymentMethodId(cryptoCode, BitcoinPaymentType.Instance).ToString();
        activeProcessor.Processor = _onChainAutomatedTransferSenderFactory.Processor;
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

    public class OnChainTransferViewModel
    {
        public OnChainTransferViewModel()
        {
            
        }

        public OnChainTransferViewModel(AutomatedTransferBlob blob)
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
