using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payouts;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.PayoutProcessors.OnChain;

public class UIOnChainAutomatedPayoutProcessorsController : Controller
{
    private readonly EventAggregator _eventAggregator;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly OnChainAutomatedPayoutSenderFactory _onChainAutomatedPayoutSenderFactory;
    private readonly PayoutProcessorService _payoutProcessorService;

    public IStringLocalizer StringLocalizer { get; }

    public UIOnChainAutomatedPayoutProcessorsController(
        EventAggregator eventAggregator,
        PaymentMethodHandlerDictionary handlers,
        OnChainAutomatedPayoutSenderFactory onChainAutomatedPayoutSenderFactory,
        IStringLocalizer stringLocalizer,
        PayoutProcessorService payoutProcessorService)
    {
        _eventAggregator = eventAggregator;
        _handlers = handlers;
        _onChainAutomatedPayoutSenderFactory = onChainAutomatedPayoutSenderFactory;
        _payoutProcessorService = payoutProcessorService;
        StringLocalizer = stringLocalizer;
    }
    PayoutMethodId GetPayoutMethod(string cryptoCode) => PayoutTypes.CHAIN.GetPayoutMethodId(cryptoCode);
    [HttpGet("~/stores/{storeId}/payout-processors/onchain-automated/{cryptocode}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, string cryptoCode)
    {
        var id = GetPayoutMethod(cryptoCode);
        if (!_onChainAutomatedPayoutSenderFactory.GetSupportedPayoutMethods().Any(i => id == i))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = StringLocalizer["This processor cannot handle {0}.", cryptoCode].Value
            });
            return RedirectToAction("ConfigureStorePayoutProcessors", "UiPayoutProcessors");
        }
        var wallet = HttpContext.GetStoreData().GetDerivationSchemeSettings(_handlers, cryptoCode);
        if (wallet?.IsHotWallet is not true)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = StringLocalizer["Either your {0} wallet is not configured, or it is not a hot wallet. This processor cannot function until a hot wallet is configured in your store.", cryptoCode].Value
            });
        }
        var activeProcessor =
            (await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new[] { _onChainAutomatedPayoutSenderFactory.Processor },
                    PayoutMethods = new[]
                    {
                        PayoutTypes.CHAIN.GetPayoutMethodId(cryptoCode)
                    }
                }))
            .FirstOrDefault();

        return View(new OnChainTransferViewModel(activeProcessor is null ? new OnChainAutomatedPayoutBlob() : OnChainAutomatedPayoutProcessor.GetBlob(activeProcessor)));
    }

    [HttpPost("~/stores/{storeId}/payout-processors/onchain-automated/{cryptocode}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, string cryptoCode, OnChainTransferViewModel automatedTransferBlob)
    {
        if (!ModelState.IsValid)
            return View(automatedTransferBlob);
        var id = GetPayoutMethod(cryptoCode);
        if (!_onChainAutomatedPayoutSenderFactory.GetSupportedPayoutMethods().Any(i => id == i))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = StringLocalizer["This processor cannot handle {0}.", cryptoCode].Value
            });
            return RedirectToAction("ConfigureStorePayoutProcessors", "UiPayoutProcessors");
        }
        var activeProcessor =
            (await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new[] { OnChainAutomatedPayoutSenderFactory.ProcessorName },
                    PayoutMethods = new[]
                    {
                        PayoutTypes.CHAIN.GetPayoutMethodId(cryptoCode)
                    }
                }))
            .FirstOrDefault();
        activeProcessor ??= new PayoutProcessorData();
        activeProcessor.HasTypedBlob<OnChainAutomatedPayoutBlob>().SetBlob(automatedTransferBlob.ToBlob());
        activeProcessor.StoreId = storeId;
        activeProcessor.PayoutMethodId = PayoutTypes.CHAIN.GetPayoutMethodId(cryptoCode).ToString();
        activeProcessor.Processor = _onChainAutomatedPayoutSenderFactory.Processor;
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
            Message = StringLocalizer["Processor updated."].Value
        });
        await tcs.Task;
        return RedirectToAction("ConfigureStorePayoutProcessors", "UiPayoutProcessors", new { storeId });
    }

    public class OnChainTransferViewModel
    {
        public OnChainTransferViewModel()
        {

        }

        public OnChainTransferViewModel(OnChainAutomatedPayoutBlob blob)
        {
            ProcessNewPayoutsInstantly = blob.ProcessNewPayoutsInstantly;
            IntervalMinutes = blob.Interval.TotalMinutes;
            FeeTargetBlock = blob.FeeTargetBlock;
            Threshold = blob.Threshold;
        }

        [Display(Name = "Process approved payouts instantly")]
        public bool ProcessNewPayoutsInstantly { get; set; }

        [Range(1, 1000)]
        public int FeeTargetBlock { get; set; }
        public decimal Threshold { get; set; }

        [Range(AutomatedPayoutConstants.MinIntervalMinutes, AutomatedPayoutConstants.MaxIntervalMinutes)]
        public double IntervalMinutes { get; set; }

        public OnChainAutomatedPayoutBlob ToBlob()
        {
            return new OnChainAutomatedPayoutBlob
            {
                ProcessNewPayoutsInstantly = ProcessNewPayoutsInstantly,
                FeeTargetBlock = FeeTargetBlock,
                Interval = TimeSpan.FromMinutes(IntervalMinutes),
                Threshold = Threshold
            };
        }
    }
}
