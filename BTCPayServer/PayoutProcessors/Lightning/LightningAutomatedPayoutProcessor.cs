using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Data.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.PayoutProcessors.Settings;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using LNURL;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayoutData = BTCPayServer.Data.PayoutData;
using PayoutProcessorData = BTCPayServer.Data.Data.PayoutProcessorData;

namespace BTCPayServer.PayoutProcessors.Lightning;

public class LightningAutomatedPayoutProcessor : BaseAutomatedPayoutProcessor<AutomatedPayoutBlob>
{
    private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
    private readonly LightningClientFactoryService _lightningClientFactoryService;
    private readonly UserService _userService;
    private readonly IOptions<LightningNetworkOptions> _options;
    private readonly LightningLikePayoutHandler _payoutHandler;
    private readonly BTCPayNetwork _network;

    public LightningAutomatedPayoutProcessor(
        BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
        LightningClientFactoryService lightningClientFactoryService,
        IEnumerable<IPayoutHandler> payoutHandlers,
        UserService userService,
        ILoggerFactory logger, IOptions<LightningNetworkOptions> options,
        StoreRepository storeRepository, PayoutProcessorData payoutProcesserSettings,
        ApplicationDbContextFactory applicationDbContextFactory, BTCPayNetworkProvider btcPayNetworkProvider) :
        base(logger, storeRepository, payoutProcesserSettings, applicationDbContextFactory,
            btcPayNetworkProvider)
    {
        _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
        _lightningClientFactoryService = lightningClientFactoryService;
        _userService = userService;
        _options = options;
        _payoutHandler = (LightningLikePayoutHandler)payoutHandlers.FindPayoutHandler(PaymentMethodId);

        _network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(_PayoutProcesserSettings.GetPaymentMethodId().CryptoCode);
    }

    protected override async Task Process(ISupportedPaymentMethod paymentMethod, PayoutData[] payouts)
    {
        await using var ctx = _applicationDbContextFactory.CreateContext();


        var lightningSupportedPaymentMethod = (LightningSupportedPaymentMethod)paymentMethod;

        if (lightningSupportedPaymentMethod.IsInternalNode &&
            !(await Task.WhenAll((await _storeRepository.GetStoreUsers(_PayoutProcesserSettings.StoreId))
                .Where(user => user.Role == StoreRoles.Owner).Select(user => user.Id)
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
            var claim = await _payoutHandler.ParseClaimDestination(PaymentMethodId, blob.Destination);
            try
            {
                switch (claim.destination)
                {
                    case LNURLPayClaimDestinaton lnurlPayClaimDestinaton:
                        var endpoint = LNURL.LNURL.Parse(lnurlPayClaimDestinaton.LNURL, out var tag);
                        var httpClient = _payoutHandler.CreateClient(endpoint);
                        var lnurlInfo =
                            (LNURLPayRequest)await LNURL.LNURL.FetchInformation(endpoint, "payRequest",
                                httpClient);
                        var lm = new LightMoney(blob.CryptoAmount.Value, LightMoneyUnit.BTC);
                        if (lm > lnurlInfo.MaxSendable || lm < lnurlInfo.MinSendable)
                        {
                            continue;
                        }
                        else
                        {
                            try
                            {
                                var lnurlPayRequestCallbackResponse =
                                    await lnurlInfo.SendRequest(lm, _network.NBitcoinNetwork, httpClient);

                                if (await TrypayBolt(client, blob, payoutData,
                                        lnurlPayRequestCallbackResponse
                                            .GetPaymentRequest(_network.NBitcoinNetwork)))
                                {
                                    ctx.Attach(payoutData);
                                    payoutData.State = PayoutState.Completed;
                                }
                            }
                            catch (LNUrlException)
                            {
                                continue;
                            }
                        }

                        break;

                    case BoltInvoiceClaimDestination item1:
                        if (await TrypayBolt(client, blob, payoutData, item1.PaymentRequest))
                        {
                            ctx.Attach(payoutData);
                            payoutData.State = PayoutState.Completed;
                        }

                        break;
                }
            }
            catch (Exception e)
            {
                Logs.PayServer.LogError(e, $"Could not process payout {payoutData.Id}");
            }
        }


        await ctx.SaveChangesAsync();
    }
    
    //we group per store and init the transfers by each
    async Task<bool> TrypayBolt(ILightningClient lightningClient, PayoutBlob payoutBlob, PayoutData payoutData,
        BOLT11PaymentRequest bolt11PaymentRequest)
    {
        var boltAmount = bolt11PaymentRequest.MinimumAmount.ToDecimal(LightMoneyUnit.BTC);
        if (boltAmount != payoutBlob.CryptoAmount)
        {
            return false;
        }

        var result = await lightningClient.Pay(bolt11PaymentRequest.ToString());
        return result.Result == PayResult.Ok;
    }
}
