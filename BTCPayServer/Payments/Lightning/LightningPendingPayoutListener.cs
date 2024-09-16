using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayoutData = BTCPayServer.Data.PayoutData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Payments.Lightning;

public class LightningPendingPayoutListener : BaseAsyncService
{
    private readonly LightningClientFactoryService _lightningClientFactoryService;
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly StoreRepository _storeRepository;
    private readonly IOptions<LightningNetworkOptions> _options;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly PaymentMethodHandlerDictionary _handlers;
    public static int SecondsDelay = 60 * 10;

    public LightningPendingPayoutListener(
        LightningClientFactoryService lightningClientFactoryService,
        ApplicationDbContextFactory applicationDbContextFactory,
        PullPaymentHostedService pullPaymentHostedService,
        StoreRepository storeRepository,
        IOptions<LightningNetworkOptions> options,
        BTCPayNetworkProvider networkProvider,
        PayoutMethodHandlerDictionary payoutHandlers,
        PaymentMethodHandlerDictionary handlers,
        ILogger<LightningPendingPayoutListener> logger) : base(logger)
    {
        _lightningClientFactoryService = lightningClientFactoryService;
        _applicationDbContextFactory = applicationDbContextFactory;
        _pullPaymentHostedService = pullPaymentHostedService;
        _storeRepository = storeRepository;
        _options = options;

        _networkProvider = networkProvider;
        _payoutHandlers = payoutHandlers;
        _handlers = handlers;
    }

    private async Task Act()
    {
        await using var context = _applicationDbContextFactory.CreateContext();
        var networks = _networkProvider.GetAll()
            .OfType<BTCPayNetwork>()
            .Where(network => network.SupportLightning)
            .ToDictionary(network => PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode));


        var payouts = await PullPaymentHostedService.GetPayouts(
            new PullPaymentHostedService.PayoutQuery()
            {
                States = new PayoutState[] { PayoutState.InProgress },
                PayoutMethods = networks.Keys.Select(id => id.ToString()).ToArray()
            }, context);
        var storeIds = payouts.Select(data => data.StoreDataId).Distinct();
        var stores = (await Task.WhenAll(storeIds.Select(_storeRepository.FindStore)))
            .Where(data => data is not null).ToDictionary(data => data.Id, data => (StoreData)data);

        foreach (IGrouping<string, PayoutData> payoutByStore in payouts.GroupBy(data => data.StoreDataId))
        {
            //this should never happen
            if (!stores.TryGetValue(payoutByStore.Key, out var store))
            {
                foreach (PayoutData payoutData in payoutByStore)
                {
                    payoutData.State = PayoutState.Cancelled;
                }

                continue;
            }

            foreach (IGrouping<string, PayoutData> payoutByStoreByPaymentMethod in payoutByStore.GroupBy(data =>
                         data.PayoutMethodId))
            {
                var pmi = PaymentMethodId.Parse(payoutByStoreByPaymentMethod.Key);
                var pm = store.GetPaymentMethodConfigs(_handlers)
                    .Where(c => c.Value is LightningPaymentMethodConfig && c.Key == pmi)
                    .Select(c => (LightningPaymentMethodConfig)c.Value)
                    .FirstOrDefault();
                if (pm is null)
                {
                    continue;
                }

                var client =
                    pm.CreateLightningClient(networks[pmi], _options.Value, _lightningClientFactoryService);
                foreach (PayoutData payoutData in payoutByStoreByPaymentMethod)
                {
                    var handler = _payoutHandlers.TryGet(payoutData.GetPayoutMethodId());
                    var proof = handler is null ? null : handler.ParseProof(payoutData);
                    switch (proof)
                    {
                        case null:
                            break;
                        case PayoutLightningBlob payoutLightningBlob:
                            {
                                LightningPayment payment = null;
                                try
                                {
                                    payment = await client.GetPayment(payoutLightningBlob.Id, CancellationToken);
                                }
                                catch
                                {
                                }
                                if (payment is null)
                                    continue;
                                switch (payment.Status)
                                {
                                    case LightningPaymentStatus.Complete:
                                        payoutData.State = PayoutState.Completed;
                                        payoutLightningBlob.Preimage = payment.Preimage;
                                        payoutData.SetProofBlob(payoutLightningBlob, null);
                                        break;
                                    case LightningPaymentStatus.Failed:
                                        payoutData.State = PayoutState.Cancelled;
                                        break;
                                }

                                break;
                            }
                    }
                }
            }
        }

        await context.SaveChangesAsync(CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(SecondsDelay), CancellationToken);
    }

    internal override Task[] InitializeTasks()
    {
        return new[] { CreateLoopTask(Act) };
    }
}
