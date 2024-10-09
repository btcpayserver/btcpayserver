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
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using MarkPayoutRequest = BTCPayServer.HostedServices.MarkPayoutRequest;
using PayoutData = BTCPayServer.Data.PayoutData;
using PayoutProcessorData = BTCPayServer.Data.PayoutProcessorData;

namespace BTCPayServer.PayoutProcessors.Lightning;

public class LightningAutomatedPayoutProcessor : BaseAutomatedPayoutProcessor<LightningAutomatedPayoutBlob>
{
    private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
    private readonly LightningClientFactoryService _lightningClientFactoryService;
    private readonly UserService _userService;
    private readonly IOptions<LightningNetworkOptions> _options;
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly LightningLikePayoutHandler _payoutHandler;
    public BTCPayNetwork Network => _payoutHandler.Network;
    private readonly PaymentMethodHandlerDictionary _handlers;

    public LightningAutomatedPayoutProcessor(
        PayoutMethodId payoutMethodId,
        BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
        LightningClientFactoryService lightningClientFactoryService,
        PayoutMethodHandlerDictionary payoutHandlers,
        UserService userService,
        ILoggerFactory logger, IOptions<LightningNetworkOptions> options,
        StoreRepository storeRepository, PayoutProcessorData payoutProcessorSettings,
        ApplicationDbContextFactory applicationDbContextFactory, 
        PaymentMethodHandlerDictionary handlers,
        IPluginHookService pluginHookService,
        EventAggregator eventAggregator,
        PullPaymentHostedService pullPaymentHostedService) :
        base(PaymentTypes.LN.GetPaymentMethodId(GetPayoutHandler(payoutHandlers, payoutMethodId).Network.CryptoCode), logger, storeRepository, payoutProcessorSettings, applicationDbContextFactory,
            handlers, pluginHookService, eventAggregator)
    {
        _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
        _lightningClientFactoryService = lightningClientFactoryService;
        _userService = userService;
        _options = options;
        _pullPaymentHostedService = pullPaymentHostedService;
        _payoutHandler = GetPayoutHandler(payoutHandlers, payoutMethodId);
        _handlers = handlers;
    }
    private static LightningLikePayoutHandler GetPayoutHandler(PayoutMethodHandlerDictionary payoutHandlers, PayoutMethodId payoutMethodId)
    {
        return (LightningLikePayoutHandler)payoutHandlers[payoutMethodId];
    }

    private async Task HandlePayout(PayoutData payoutData, ILightningClient lightningClient)
    {
        if (payoutData.State != PayoutState.AwaitingPayment)
            return;
        var res = await _pullPaymentHostedService.MarkPaid(new MarkPayoutRequest()
        {
            State = PayoutState.InProgress, PayoutId = payoutData.Id, Proof = null
        });
        if (res != MarkPayoutRequest.PayoutPaidResult.Ok)
        {
            return;
        }

        var blob = payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings);
        try
        {
			var claim = await _payoutHandler.ParseClaimDestination(blob.Destination, CancellationToken);
			switch (claim.destination)
            {
                case LNURLPayClaimDestinaton lnurlPayClaimDestinaton:
                    var lnurlResult = await UILightningLikePayoutController.GetInvoiceFromLNURL(payoutData,
                        _payoutHandler, blob,
                        lnurlPayClaimDestinaton, Network.NBitcoinNetwork, CancellationToken);
                    if (lnurlResult.Item2 is null)
                    {
                        await TrypayBolt(lightningClient, blob, payoutData,
                            lnurlResult.Item1);
                    }
                    break;
                case BoltInvoiceClaimDestination item1:
                    await TrypayBolt(lightningClient, blob, payoutData, item1.PaymentRequest);
                    break;
            }
        }
        catch (Exception e)
        {
            Logs.PayServer.LogError(e, $"Could not process payout {payoutData.Id}");
        }

        if (payoutData.State != PayoutState.InProgress || payoutData.Proof is not null)
        {
            await _pullPaymentHostedService.MarkPaid(new MarkPayoutRequest()
            {
                State = payoutData.State, PayoutId = payoutData.Id, Proof = payoutData.GetProofBlobJson()
            });
        }
    }

    protected override async Task<bool> ProcessShouldSave(object paymentMethodConfig, List<PayoutData> payouts)
    {
		var processorBlob = GetBlob(PayoutProcessorSettings);
        var lightningSupportedPaymentMethod = (LightningPaymentMethodConfig)paymentMethodConfig;
        if (lightningSupportedPaymentMethod.IsInternalNode &&
            !await _storeRepository.InternalNodePayoutAuthorized(PayoutProcessorSettings.StoreId))
        {
            return false;
        }

        var client =
            lightningSupportedPaymentMethod.CreateLightningClient(Network, _options.Value,
                _lightningClientFactoryService);
        await Task.WhenAll(payouts.Select(data => HandlePayout(data, client)));

        //we return false because this processor handles db updates on its own
        return false;
    }

    //we group per store and init the transfers by each
    async Task<bool> TrypayBolt(ILightningClient lightningClient, PayoutBlob payoutBlob, PayoutData payoutData,
        BOLT11PaymentRequest bolt11PaymentRequest)
    {
        return (await UILightningLikePayoutController.TrypayBolt(lightningClient, payoutBlob, payoutData,
            bolt11PaymentRequest,
			CancellationToken)).Result is  PayResult.Ok ;
    }
}
