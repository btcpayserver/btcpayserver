using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using PayoutData = BTCPayServer.Data.PayoutData;
using PayoutProcessorData = BTCPayServer.Data.PayoutProcessorData;

namespace BTCPayServer.PayoutProcessors.Lightning;

public class LightningAutomatedPayoutProcessor : BaseAutomatedPayoutProcessor<LightningAutomatedPayoutBlob>
{
    private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
    private readonly LightningClientFactoryService _lightningClientFactoryService;
    private readonly UserService _userService;
    private readonly IOptions<LightningNetworkOptions> _options;
    private readonly LightningLikePayoutHandler _payoutHandler;
    private readonly BTCPayNetwork _network;
    private readonly ConcurrentDictionary<string, int> _failedPayoutCounter = new();

    public LightningAutomatedPayoutProcessor(
        BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
        LightningClientFactoryService lightningClientFactoryService,
        IEnumerable<IPayoutHandler> payoutHandlers,
        UserService userService,
        ILoggerFactory logger, IOptions<LightningNetworkOptions> options,
        StoreRepository storeRepository, PayoutProcessorData payoutProcessorSettings,
        ApplicationDbContextFactory applicationDbContextFactory, 
        BTCPayNetworkProvider btcPayNetworkProvider,
        IPluginHookService pluginHookService,
        EventAggregator eventAggregator) :
        base(logger, storeRepository, payoutProcessorSettings, applicationDbContextFactory,
            btcPayNetworkProvider, pluginHookService, eventAggregator)
    {
        _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
        _lightningClientFactoryService = lightningClientFactoryService;
        _userService = userService;
        _options = options;
        _payoutHandler = (LightningLikePayoutHandler)payoutHandlers.FindPayoutHandler(PaymentMethodId);

        _network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(PayoutProcessorSettings.GetPaymentMethodId().CryptoCode);
    }

    protected override async Task Process(ISupportedPaymentMethod paymentMethod, List<PayoutData> payouts)
    {
        var processorBlob = GetBlob(PayoutProcessorSettings);
        var lightningSupportedPaymentMethod = (LightningSupportedPaymentMethod)paymentMethod;
        if (lightningSupportedPaymentMethod.IsInternalNode &&
            !(await Task.WhenAll((await _storeRepository.GetStoreUsers(PayoutProcessorSettings.StoreId))
                .Where(user => user.StoreRole.ToPermissionSet( PayoutProcessorSettings.StoreId).Contains(Policies.CanModifyStoreSettings, PayoutProcessorSettings.StoreId)).Select(user => user.Id)
                .Select(s => _userService.IsAdminUser(s)))).Any(b => b))
        {
            return;
        }
        var client =
            lightningSupportedPaymentMethod.CreateLightningClient(_network, _options.Value,
                _lightningClientFactoryService);

        foreach (var payoutData in payouts)
        {
            var blob = payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings);
            var failed = false;
            var claim = await _payoutHandler.ParseClaimDestination(PaymentMethodId, blob.Destination, CancellationToken);
            try
            {
                switch (claim.destination)
                {
                    case LNURLPayClaimDestinaton lnurlPayClaimDestinaton:
                        var lnurlResult = await UILightningLikePayoutController.GetInvoiceFromLNURL(payoutData,
                            _payoutHandler, blob,
                            lnurlPayClaimDestinaton, _network.NBitcoinNetwork, CancellationToken);
                        if (lnurlResult.Item2 is not null)
                        {
                            continue;
                        }
                        failed = await TrypayBolt(client, blob, payoutData,
                            lnurlResult.Item1);
                        break;
                    case BoltInvoiceClaimDestination item1:
                        failed = await TrypayBolt(client, blob, payoutData, item1.PaymentRequest);
                        break;
                }
            }
            catch (Exception e)
            {
                Logs.PayServer.LogError(e, $"Could not process payout {payoutData.Id}");
                failed = true;
            }

            if (failed && processorBlob.CancelPayoutAfterFailures is not null)
            {
                if (!_failedPayoutCounter.TryGetValue(payoutData.Id, out int counter))
                {
                    counter = 0;
                }
                counter++;
                if(counter >= processorBlob.CancelPayoutAfterFailures)
                {
                    payoutData.State = PayoutState.Cancelled;
                    Logs.PayServer.LogError($"Payout {payoutData.Id} has failed {counter} times, cancelling it");
                }
                else
                {
                    _failedPayoutCounter.AddOrReplace(payoutData.Id, counter);
                }
            }
            if (payoutData.State == PayoutState.Cancelled)
            {
                _failedPayoutCounter.TryRemove(payoutData.Id, out _);
            }
        }
    }

    //we group per store and init the transfers by each
    async Task<bool> TrypayBolt(ILightningClient lightningClient, PayoutBlob payoutBlob, PayoutData payoutData,
        BOLT11PaymentRequest bolt11PaymentRequest)
    {
        return (await UILightningLikePayoutController.TrypayBolt(lightningClient, payoutBlob, payoutData, bolt11PaymentRequest,
            payoutData.GetPaymentMethodId(), CancellationToken)).Result == PayResult.Ok;
    }
}
