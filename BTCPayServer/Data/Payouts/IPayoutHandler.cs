using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;

public interface IPayoutHandler
{
    public bool CanHandle(PaymentMethodId paymentMethod);
    //Allows payout handler to parse payout destinations on its own
    public Task<IClaimDestination> ParseClaimDestination(PaymentMethodId paymentMethodId, string destination);
    public IPayoutProof ParseProof(PayoutData payout);
    //Allows you to subscribe the main pull payment hosted service to events and prepare the handler 
    void StartBackgroundCheck(Action<Type[]> subscribe);
    //allows you to process events that the main pull payment hosted service is subscribed to
    Task BackgroundCheck(object o);
    Task<decimal> GetMinimumPayoutAmount(PaymentMethodId paymentMethod, IClaimDestination claimDestination);
}
