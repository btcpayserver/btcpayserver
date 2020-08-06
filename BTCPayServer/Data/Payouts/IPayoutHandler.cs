using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;

public interface IPayoutHandler
{
    public bool CanHandle(PaymentMethodId paymentMethod);
    public Task TrackClaim(PaymentMethodId paymentMethodId, IClaimDestination claimDestination);
    public Task<IClaimDestination> ParseClaimDestination(PaymentMethodId paymentMethodId, string destination);
    public IPayoutProof ParseProof(PayoutData payout);
    void StartBackgroundCheck(Action<Type[]> subscribe);
    Task BackgroundCheck(object o);
    Task<decimal> GetMinimumPayoutAmount(PaymentMethodId paymentMethod, IClaimDestination claimDestination);
    Dictionary<PayoutState, List<(string Action, string Text)>> GetPayoutSpecificActions();
    Task<StatusMessageModel> DoSpecificAction(string action, string[] payoutIds, string storeId);
}
