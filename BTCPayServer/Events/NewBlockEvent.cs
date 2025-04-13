using BTCPayServer.Payments;

namespace BTCPayServer.Events
{
    public class NewBlockEvent
    {
        public PaymentMethodId PaymentMethodId { get; set; }
        public object AdditionalInfo { get; set; }
        public override string ToString()
        {
            return $"{PaymentMethodId}: New block";
        }
    }
}
 
