using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.PayoutProcessors.OnChain;

public class UIOnChainAutomatedPayoutProcessorsController : Controller
{
    private readonly EventAggregator _eventAggregator;
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly OnChainAutomatedPayoutSenderFactory _onChainAutomatedPayoutSenderFactory;
    private readonly PayoutProcessorService _payoutProcessorService;

    public UIOnChainAutomatedPayoutProcessorsController(
        EventAggregator eventAggregator,
        BTCPayNetworkProvider btcPayNetworkProvider,
        OnChainAutomatedPayoutSenderFactory onChainAutomatedPayoutSenderFactory,
        PayoutProcessorService payoutProcessorService)
    {
        _eventAggregator = eventAggregator;
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _onChainAutomatedPayoutSenderFactory = onChainAutomatedPayoutSenderFactory;
        _payoutProcessorService = payoutProcessorService;
    }


    [HttpGet("~/stores/{storeId}/payout-processors/onchain-automated/{cryptocode}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, string cryptoCode)
    {
        if (!_onChainAutomatedPayoutSenderFactory.GetSupportedPaymentMethods().Any(id =>
                id.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase)))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = $"This processor cannot handle {cryptoCode}."
            });
            return RedirectToAction("ConfigureStorePayoutProcessors", "UiPayoutProcessors");
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
            (await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new[] { _onChainAutomatedPayoutSenderFactory.Processor },
                    PaymentMethods = new[]
                    {
                        new PaymentMethodId(cryptoCode, BitcoinPaymentType.Instance).ToString()
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
        if (!_onChainAutomatedPayoutSenderFactory.GetSupportedPaymentMethods().Any(id =>
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
                    Processors = new[] { OnChainAutomatedPayoutSenderFactory.ProcessorName },
                    PaymentMethods = new[]
                    {
                        new PaymentMethodId(cryptoCode, BitcoinPaymentType.Instance).ToString()
                    }
                }))
            .FirstOrDefault();
        activeProcessor ??= new PayoutProcessorData();
        activeProcessor.HasTypedBlob<OnChainAutomatedPayoutBlob>().SetBlob(automatedTransferBlob.ToBlob());
        activeProcessor.StoreId = storeId;
        activeProcessor.PaymentMethod = new PaymentMethodId(cryptoCode, BitcoinPaymentType.Instance).ToString();
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
            Message = "Processor updated."
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
