using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;

public interface IPayoutHandler
{
    public bool CanHandle(PaymentMethodId paymentMethod);
    public Task<IClaimDestination> ParseClaimDestination(PaymentMethodId paymentMethodId, string destination);
    public IPayoutProof ParseProof(PayoutData payout);
    void StartBackgroundCheck(Action<Type[]> subscribe);
    Task BackgroundCheck(object o);
    Task<decimal> GetMinimumPayoutAmount(PaymentMethodId paymentMethod, IClaimDestination claimDestination);
}
