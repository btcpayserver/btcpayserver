#nullable  enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using static BTCPayServer.PayoutProcessors.PayoutProcessorService;
using PayoutData = BTCPayServer.Data.PayoutData;
using PayoutProcessorData = BTCPayServer.Data.PayoutProcessorData;

namespace BTCPayServer.PayoutProcessors;

public abstract class BaseAutomatedPayoutProcessor<T> : EventHostedServiceBase where T : AutomatedPayoutBlob, new()
{
    protected readonly StoreRepository _storeRepository;
    protected readonly PayoutProcessorData PayoutProcessorSettings;
    protected readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    protected readonly PayoutMethodId PayoutMethodId;
    protected readonly PaymentMethodId PaymentMethodId;
    private readonly IPluginHookService _pluginHookService;

    protected BaseAutomatedPayoutProcessor(
        PaymentMethodId paymentMethodId,
        ILoggerFactory logger,
        StoreRepository storeRepository,
        PayoutProcessorData payoutProcessorSettings,
        ApplicationDbContextFactory applicationDbContextFactory,
        PaymentMethodHandlerDictionary paymentHandlers,
        IPluginHookService pluginHookService,
        EventAggregator eventAggregator) : base(eventAggregator, logger.CreateLogger($"{payoutProcessorSettings.Processor}:{payoutProcessorSettings.StoreId}:{payoutProcessorSettings.PayoutMethodId}"))
    {
        PaymentMethodId = paymentMethodId;
        _storeRepository = storeRepository;
        PayoutProcessorSettings = payoutProcessorSettings;
        PayoutMethodId = PayoutProcessorSettings.GetPayoutMethodId();
        _applicationDbContextFactory = applicationDbContextFactory;
        _paymentHandlers = paymentHandlers;
        _pluginHookService = pluginHookService;
    }

    protected override void SubscribeToEvents()
    {
        this.Subscribe<PayoutEvent>();
        this.Subscribe<PayoutProcessorService.AwaitingPayoutsEvent>();
        this.Subscribe<PayoutProcessorService.PollProcessorEvent>();
    }

    protected virtual Task Process(object paymentMethodConfig, List<PayoutData> payouts) =>
        throw new NotImplementedException();

    protected virtual async Task<bool> ProcessShouldSave(object paymentMethodConfig, List<PayoutData> payouts)
    {
        await Process(paymentMethodConfig, payouts);
        return true;
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        PullPaymentHostedService.PayoutQuery query = new PullPaymentHostedService.PayoutQuery()
        {
            States = new[] { PayoutState.AwaitingPayment },
            PayoutMethods = new[] { PayoutProcessorSettings.PayoutMethodId },
            Processor = PayoutProcessorSettings.Processor,
            Stores = new[] { PayoutProcessorSettings.StoreId }
        };
        List<PayoutData> payouts = new();
        if (evt is PayoutEvent pe)
        {
            if (!ProcessInstantly(pe))
                return;
            payouts.Add(pe.Payout);
        }
        else if (evt is PayoutProcessorService.AwaitingPayoutsEvent ape)
        {
            if (!ape.PayoutsByStoreId.TryGetValue(PayoutProcessorSettings.StoreId, out var p))
                return;
            payouts = p;
        }
        else if (evt is PayoutProcessorService.PollProcessorEvent poll)
        {
            if (poll.ProcessorId != PayoutProcessorSettings.Id)
                return;
            await using var context = _applicationDbContextFactory.CreateContext();
            payouts = await PullPaymentHostedService.GetPayouts(
            new PullPaymentHostedService.PayoutQuery()
            {
                States = new[] { PayoutState.AwaitingPayment },
                PayoutMethods = new[] { PayoutProcessorSettings.PayoutMethodId },
                Processor = PayoutProcessorSettings.Processor,
                Stores = new[] { PayoutProcessorSettings.StoreId }
            }, context, CancellationToken);
        }
        payouts = payouts
            .Where(p => p.GetBlob(null).DisabledProcessors?.Contains(PayoutProcessorSettings.Processor) is not true)
            .ToList();
        if (payouts.Count == 0)
            return;
        var store = await _storeRepository.FindStore(PayoutProcessorSettings.StoreId);
        var paymentMethod = store?.GetPaymentMethodConfig(PaymentMethodId, _paymentHandlers, true);
        var blob = GetBlob(PayoutProcessorSettings);
        if (paymentMethod is not null)
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            foreach (var payout in payouts)
                context.Payouts.Attach(payout);

            await _pluginHookService.ApplyAction("before-automated-payout-processing",
                new BeforePayoutActionData(store, PayoutProcessorSettings, payouts));
            if (payouts.Any())
            {
                if (await ProcessShouldSave(paymentMethod, payouts))
                {
                    await context.SaveChangesAsync();

                    foreach (var payoutData in payouts.Where(payoutData => payoutData.State != PayoutState.AwaitingPayment))
                    {
                        EventAggregator.Publish(new PayoutEvent(PayoutEvent.PayoutEventType.Updated, payoutData));
                    }
                }

            }

            // Allow plugins do to something after automatic payout processing
            await _pluginHookService.ApplyAction("after-automated-payout-processing",
                new AfterPayoutActionData(store, PayoutProcessorSettings, payouts));
        }
    }

    private bool ProcessInstantly(PayoutEvent evt) =>
        evt is { Type: PayoutEvent.PayoutEventType.Approved } pe
                && PayoutProcessorSettings.StoreId == pe.Payout.StoreDataId
                && pe.Payout.GetPayoutMethodId() == PayoutMethodId
                && GetBlob(PayoutProcessorSettings).ProcessNewPayoutsInstantly;

    public static T GetBlob(PayoutProcessorData payoutProcesserSettings)
    {
        return payoutProcesserSettings.HasTypedBlob<T>().GetBlob() ?? new T();
    }
}
