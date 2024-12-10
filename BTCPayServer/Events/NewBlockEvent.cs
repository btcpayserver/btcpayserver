using BTCPayServer.Payments;

namespace BTCPayServer.Events
{
    public class NewBlockEvent : NBXplorer.Models.NewBlockEvent
    {
        public PaymentMethodId PaymentMethodId { get; set; }
        public override string ToString()
        {
            return $"{PaymentMethodId}: New block";
        }
    }
}
 
