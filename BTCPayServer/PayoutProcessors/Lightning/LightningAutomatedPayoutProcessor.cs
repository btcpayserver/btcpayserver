using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
using LNURL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json.Linq;
using static BTCPayServer.Data.Payouts.LightningLike.UILightningLikePayoutController;
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

    public async Task<ResultVM> HandlePayout(PayoutData payoutData, ILightningClient lightningClient, CancellationToken cancellationToken)
	{
        using var scope = _payoutHandler.PayoutsPaymentProcessing.StartTracking();
		if (payoutData.State != PayoutState.AwaitingPayment || !scope.TryTrack(payoutData.Id))
			return InvalidState(payoutData.Id);
        var blob = payoutData.GetBlob(_btcPayNetworkJsonSerializerSettings);
		var res = await _pullPaymentHostedService.MarkPaid(new MarkPayoutRequest()
		{
			State = PayoutState.InProgress,
			PayoutId = payoutData.Id,
			Proof = null
		});
		if (res != MarkPayoutRequest.PayoutPaidResult.Ok)
            return InvalidState(payoutData.Id);
        ResultVM result;
        var claim = await _payoutHandler.ParseClaimDestination(blob.Destination, cancellationToken);
        switch (claim.destination)
        {
            case LNURLPayClaimDestinaton lnurlPayClaimDestinaton:
                var lnurlResult = await GetInvoiceFromLNURL(payoutData, _payoutHandler, blob,
                    lnurlPayClaimDestinaton, cancellationToken);
                if (lnurlResult.Item2 is not null)
                {
                    result = lnurlResult.Item2;
                }
                else
                {
                    result = await TrypayBolt(lightningClient, blob, payoutData, lnurlResult.Item1, cancellationToken);
                }
                break;

            case BoltInvoiceClaimDestination item1:
                result = await TrypayBolt(lightningClient, blob, payoutData, item1.PaymentRequest, cancellationToken);
                break;
            default:
                result = new ResultVM
                {
                    PayoutId = payoutData.Id,
                    Success = false,
                    Destination = blob.Destination,
                    Message = claim.error
                };
                break;
        }

        if (result.Success is false && blob.NonInteractiveOnly)
            payoutData.State = PayoutState.Cancelled;

        bool updateBlob = false;
		if (result.Success is false && payoutData.State == PayoutState.AwaitingPayment)
		{
			updateBlob = true;
            if (blob.IncrementErrorCount() >= 10)
                blob.DisableProcessor(LightningAutomatedPayoutSenderFactory.ProcessorName);
		}
		if (payoutData.State != PayoutState.InProgress || payoutData.Proof is not null)
		{
			await _pullPaymentHostedService.MarkPaid(new MarkPayoutRequest()
			{
				State = payoutData.State,
				PayoutId = payoutData.Id,
				Proof = payoutData.GetProofBlobJson(),
				UpdateBlob = updateBlob ? blob : null
			});
		}
        return result;
	}

    private ResultVM InvalidState(string payoutId) =>
        new ResultVM
        {
            PayoutId = payoutId,
            Success = false,
            Message = "The payout isn't in a valid state"
        };

    async Task<(BOLT11PaymentRequest, ResultVM)> GetInvoiceFromLNURL(PayoutData payoutData,
            LightningLikePayoutHandler handler, PayoutBlob blob, LNURLPayClaimDestinaton lnurlPayClaimDestinaton, CancellationToken cancellationToken)
    {
        var endpoint = lnurlPayClaimDestinaton.LNURL.IsValidEmail()
            ? LNURL.LNURL.ExtractUriFromInternetIdentifier(lnurlPayClaimDestinaton.LNURL)
            : LNURL.LNURL.Parse(lnurlPayClaimDestinaton.LNURL, out _);
        var httpClient = handler.CreateClient(endpoint);
        var lnurlInfo =
            (LNURLPayRequest)await LNURL.LNURL.FetchInformation(endpoint, "payRequest",
                httpClient, cancellationToken);
        var lm = new LightMoney(payoutData.Amount.Value, LightMoneyUnit.BTC);
        if (lm > lnurlInfo.MaxSendable || lm < lnurlInfo.MinSendable)
        {

            payoutData.State = PayoutState.Cancelled;
            return (null, new ResultVM
            {
                PayoutId = payoutData.Id,
                Success = false,
                Destination = blob.Destination,
                Message =
                    $"The LNURL provided would not generate an invoice of {lm.ToDecimal(LightMoneyUnit.Satoshi)} sats"
            });
        }

        try
        {
            var lnurlPayRequestCallbackResponse =
                await lnurlInfo.SendRequest(lm, this.Network.NBitcoinNetwork, httpClient, cancellationToken: cancellationToken);

            return (lnurlPayRequestCallbackResponse.GetPaymentRequest(this.Network.NBitcoinNetwork), null);
        }
        catch (LNUrlException e)
        {
            return (null,
                new ResultVM
                {
                    PayoutId = payoutData.Id,
                    Success = false,
                    Destination = blob.Destination,
                    Message = e.Message
                });
        }
    }

    async Task<ResultVM> TrypayBolt(
            ILightningClient lightningClient, PayoutBlob payoutBlob, PayoutData payoutData, BOLT11PaymentRequest bolt11PaymentRequest, CancellationToken cancellationToken)
    {
        var boltAmount = bolt11PaymentRequest.MinimumAmount.ToDecimal(LightMoneyUnit.BTC);

        // BoltAmount == 0: Any amount is OK.
        // While we could allow paying more than the minimum amount from the boltAmount,
        // Core-Lightning do not support it! It would just refuse to pay more than the boltAmount.
        if (boltAmount != payoutData.Amount.Value && boltAmount != 0.0m)
        {
            payoutData.State = PayoutState.Cancelled;
            return new ResultVM
            {
                PayoutId = payoutData.Id,
                Success = false,
                Message = $"The BOLT11 invoice amount ({boltAmount} {payoutData.Currency}) did not match the payout's amount ({payoutData.Amount.GetValueOrDefault()} {payoutData.Currency})",
                Destination = payoutBlob.Destination
            };
        }

        if (bolt11PaymentRequest.ExpiryDate < DateTimeOffset.Now)
        {
            payoutData.State = PayoutState.Cancelled;
            return new ResultVM
            {
                PayoutId = payoutData.Id,
                Success = false,
                Message = $"The BOLT11 invoice expiry date ({bolt11PaymentRequest.ExpiryDate}) has expired",
                Destination = payoutBlob.Destination
            };
        }

        var proofBlob = new PayoutLightningBlob { PaymentHash = bolt11PaymentRequest.PaymentHash.ToString() };
        string errorReason = null;
        string preimage = null;
        // If success:
        // * Is null, we don't know the status. The payout should become pending. (LightningPendingPayoutListener will monitor the situation)
        // * Is true, we knew the transfer was done. The payout should be completed.
        // * Is false, we knew it didn't happen. The payout can be retried.
        bool? success = null;
        LightMoney amountSent = null;

        try
        {
            var pay = await lightningClient.Pay(bolt11PaymentRequest.ToString(),
                new PayInvoiceParams()
                {
                    Amount = new LightMoney((decimal)payoutData.Amount, LightMoneyUnit.BTC)
                }, cancellationToken);
            if (pay is { Result: PayResult.CouldNotFindRoute })
            {
                var err = pay.ErrorDetail is null ? "" : $" ({pay.ErrorDetail})";
                errorReason ??= $"Unable to find a route for the payment, check your channel liquidity{err}";
                success = false;
            }
            else if (pay is { Result: PayResult.Error })
            {
                errorReason ??= pay.ErrorDetail;
                success = false;
            }
            else if (pay is { Result: PayResult.Ok })
            {
                if (pay.Details is { } details)
                {
                    preimage = details.Preimage?.ToString();
                    amountSent = details.TotalAmount;
                }
                success = true;
            }
        }
        catch (Exception ex)
        {
            errorReason ??= ex.Message;
        }

        if (success is null || preimage is null || amountSent is null)
        {
            LightningPayment payment = null;
            try
            {
                payment = await lightningClient.GetPayment(bolt11PaymentRequest.PaymentHash.ToString(), cancellationToken);
            }
            catch (Exception ex)
            {
                errorReason ??= ex.Message;
            }
            success ??= payment?.Status switch
            {
                LightningPaymentStatus.Complete => true,
                LightningPaymentStatus.Failed => false,
                _ => null
            };
            amountSent ??= payment?.AmountSent;
            preimage ??= payment?.Preimage;
        }

        if (preimage is not null)
            proofBlob.Preimage = preimage;
        
        var vm = new ResultVM
        {
            PayoutId = payoutData.Id,
            Success = success,
            Destination = payoutBlob.Destination
        };
        if (success is true)
        {
            payoutData.State = PayoutState.Completed;
            payoutData.SetProofBlob(proofBlob, null);
            vm.Message = amountSent != null
                    ? $"Paid out {amountSent.ToDecimal(LightMoneyUnit.BTC)} {payoutData.Currency}"
                    : "Paid out";
        }
        else if (success is false)
        {
            payoutData.State = PayoutState.AwaitingPayment;
            var err = errorReason is null ? "" : $" ({errorReason})";
            vm.Message = $"The payment failed{err}";
        }
        else
        {
            // Payment will be saved as pending, the LightningPendingPayoutListener will handle settling/cancelling
            payoutData.State = PayoutState.InProgress;
            payoutData.SetProofBlob(proofBlob, null);
            vm.Message = "The payment has been initiated but is still in-flight.";
        }
        return vm;
    }

    protected override async Task<bool> ProcessShouldSave(object paymentMethodConfig, List<PayoutData> payouts)
	{
		var lightningSupportedPaymentMethod = (LightningPaymentMethodConfig)paymentMethodConfig;
		if (lightningSupportedPaymentMethod.IsInternalNode &&
			!await _storeRepository.InternalNodePayoutAuthorized(PayoutProcessorSettings.StoreId))
		{
			return false;
		}

		var client =
			lightningSupportedPaymentMethod.CreateLightningClient(Network, _options.Value,
				_lightningClientFactoryService);
		await Task.WhenAll(payouts.Select(data => HandlePayout(data, client, CancellationToken)));

		//we return false because this processor handles db updates on its own
		return false;
	}
}
