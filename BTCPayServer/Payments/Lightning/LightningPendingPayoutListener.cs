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
using Newtonsoft.Json.Linq;
using PayoutData = BTCPayServer.Data.PayoutData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Payments.Lightning;

public class LightningPendingPayoutListener : BaseAsyncService
{
    private readonly LightningClientFactoryService _lightningClientFactoryService;
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly StoreRepository _storeRepository;
    private readonly IOptions<LightningNetworkOptions> _options;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly PaymentMethodHandlerDictionary _handlers;
    public static int SecondsDelay = 60 * 10;

    public LightningPendingPayoutListener(
        LightningClientFactoryService lightningClientFactoryService,
        PullPaymentHostedService pullPaymentHostedService,
        StoreRepository storeRepository,
        IOptions<LightningNetworkOptions> options,
        BTCPayNetworkProvider networkProvider,
        PayoutMethodHandlerDictionary payoutHandlers,
        PaymentMethodHandlerDictionary handlers,
        ILogger<LightningPendingPayoutListener> logger) : base(logger)
    {
        _lightningClientFactoryService = lightningClientFactoryService;
        _pullPaymentHostedService = pullPaymentHostedService;
        _storeRepository = storeRepository;
        _options = options;

        _networkProvider = networkProvider;
        _payoutHandlers = payoutHandlers;
        _handlers = handlers;
    }

    private async Task Act()
    {
        var networks = _networkProvider.GetAll()
            .OfType<BTCPayNetwork>()
            .Where(network => network.SupportLightning)
            .ToDictionary(network => PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode));


        var payouts = await _pullPaymentHostedService.GetPayouts(
            new PullPaymentHostedService.PayoutQuery()
            {
                States = new PayoutState[] { PayoutState.InProgress },
                PayoutMethods = networks.Keys.Select(id => id.ToString()).ToArray()
            });
        var storeIds = payouts.Select(data => data.StoreDataId).Distinct();
        var stores = (await Task.WhenAll(storeIds.Select(_storeRepository.FindStore)))
            .Where(data => data is not null).ToDictionary(data => data.Id, data => (StoreData)data);

        foreach (IGrouping<string, PayoutData> payoutByStore in payouts.GroupBy(data => data.StoreDataId))
        {
			var store = stores[payoutByStore.Key];
            foreach (IGrouping<string, PayoutData> payoutByStoreByPaymentMethod in payoutByStore.GroupBy(data =>
                         data.PayoutMethodId))
            {
                var pmi = PaymentMethodId.Parse(payoutByStoreByPaymentMethod.Key);
                var pm = store.GetPaymentMethodConfigs(_handlers)
                    .Where(c => c.Value is LightningPaymentMethodConfig && c.Key == pmi)
                    .Select(c => (LightningPaymentMethodConfig)c.Value)
                    .FirstOrDefault();
                if (pm is null)
                    continue;

                var client =
                    pm.CreateLightningClient(networks[pmi], _options.Value, _lightningClientFactoryService);
                foreach (PayoutData payoutData in payoutByStoreByPaymentMethod)
                {
                    var handler = _payoutHandlers.TryGet(payoutData.GetPayoutMethodId()) as LightningLikePayoutHandler;
					if (handler is null || handler.PayoutsPaymentProcessing.Contains(payoutData.Id))
						continue;
                    using var track = handler.PayoutsPaymentProcessing.StartTracking();
                    if (!track.TryTrack(payoutData.Id))
                        continue;
                    var proof = handler.ParseProof(payoutData) as PayoutLightningBlob;

					LightningPayment payment = null;
					try
					{
						if (proof is not null)
							payment = await client.GetPayment(proof.PaymentHash, CancellationToken);
					}
					catch (OperationCanceledException)
					{
                        // Do not mark as cancelled if the operation was cancelled.
                        // This can happen with Nostr GetPayment if the connection to relay is too slow.
                        continue;
					}
                    payoutData.State = payment?.Status switch
                    {
                        LightningPaymentStatus.Complete => PayoutState.Completed,
                        LightningPaymentStatus.Failed => PayoutState.Cancelled,
                        LightningPaymentStatus.Unknown or LightningPaymentStatus.Pending => PayoutState.InProgress,
                        _ => PayoutState.Cancelled
                    };

                    if (payment is { Status: LightningPaymentStatus.Complete })
                    {
                        proof.Preimage = payment.Preimage;
                        payoutData.SetProofBlob(proof, null);
                    }
				}

                foreach (PayoutData payoutData in payoutByStoreByPaymentMethod)
                {
                    if (payoutData.State != PayoutState.InProgress)
                    {
                        // This update can fail if the payout has been updated in the meantime
                        await _pullPaymentHostedService.MarkPaid(new HostedServices.MarkPayoutRequest()
                        {
                            PayoutId = payoutData.Id,
                            State = payoutData.State,
                            Proof = payoutData.State is PayoutState.Completed ? JObject.Parse(payoutData.Proof) : null
                        });
                    }
                }
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(SecondsDelay), CancellationToken);
    }

    internal override Task[] InitializeTasks()
    {
        return new[] { CreateLoopTask(Act) };
    }
}
