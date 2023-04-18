#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc;
using PayoutData = BTCPayServer.Data.PayoutData;
using StoreData = BTCPayServer.Data.StoreData;

public interface IPayoutHandler
{
    public bool CanHandle(PaymentMethodId paymentMethod);
    public Task TrackClaim(ClaimRequest claimRequest, PayoutData payoutData);
    //Allows payout handler to parse payout destinations on its own
    public Task<(IClaimDestination destination, string error)> ParseClaimDestination(PaymentMethodId paymentMethodId, string destination, CancellationToken cancellationToken);
    public (bool valid, string? error) ValidateClaimDestination(IClaimDestination claimDestination, PullPaymentBlob? pullPaymentBlob);
    public async Task<(IClaimDestination? destination, string? error)> ParseAndValidateClaimDestination(PaymentMethodId paymentMethodId, string destination, PullPaymentBlob? pullPaymentBlob, CancellationToken cancellationToken)
    {
        var res = await ParseClaimDestination(paymentMethodId, destination, cancellationToken);
        if (res.destination is null)
            return res;
        var res2 = ValidateClaimDestination(res.destination, pullPaymentBlob);
        if (!res2.valid)
            return (null, res2.error);
        return res;
    }
    public IPayoutProof ParseProof(PayoutData payout);
    //Allows you to subscribe the main pull payment hosted service to events and prepare the handler 
    void StartBackgroundCheck(Action<Type[]> subscribe);
    //allows you to process events that the main pull payment hosted service is subscribed to
    Task BackgroundCheck(object o);
    Task<decimal> GetMinimumPayoutAmount(PaymentMethodId paymentMethod, IClaimDestination claimDestination);
    Dictionary<PayoutState, List<(string Action, string Text)>> GetPayoutSpecificActions();
    Task<StatusMessageModel> DoSpecificAction(string action, string[] payoutIds, string storeId);
    Task<IEnumerable<PaymentMethodId>> GetSupportedPaymentMethods(StoreData storeData);
    Task<IActionResult> InitiatePayment(PaymentMethodId paymentMethodId, string[] payoutIds);
}
