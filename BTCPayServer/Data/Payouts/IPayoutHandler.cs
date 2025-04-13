#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using Microsoft.AspNetCore.Mvc;
using PayoutData = BTCPayServer.Data.PayoutData;
using StoreData = BTCPayServer.Data.StoreData;

public interface IPayoutHandler : IHandler<PayoutMethodId>
{
    PayoutMethodId IHandler<PayoutMethodId>.Id => PayoutMethodId;
    string Currency { get; }
    public PayoutMethodId PayoutMethodId { get; }
    bool IsSupported(StoreData storeData);
    public Task TrackClaim(ClaimRequest claimRequest, PayoutData payoutData);
    //Allows payout handler to parse payout destinations on its own
    public Task<(IClaimDestination destination, string error)> ParseClaimDestination(string destination, CancellationToken cancellationToken);
    public (bool valid, string? error) ValidateClaimDestination(IClaimDestination claimDestination, PullPaymentBlob? pullPaymentBlob);
    public async Task<(IClaimDestination? destination, string? error)> ParseAndValidateClaimDestination(string destination, PullPaymentBlob? pullPaymentBlob, CancellationToken cancellationToken)
    {
        var res = await ParseClaimDestination(destination, cancellationToken);
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
    Task<decimal> GetMinimumPayoutAmount(IClaimDestination claimDestination);
    Dictionary<PayoutState, List<(string Action, string Text)>> GetPayoutSpecificActions();
    Task<StatusMessageModel> DoSpecificAction(string action, string[] payoutIds, string storeId);
    Task<IActionResult> InitiatePayment(string[] payoutIds);
}
