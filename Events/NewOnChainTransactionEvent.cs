using BTCPayServer.Payments;
using NBXplorer.Models;

namespace BTCPayServer.Events
{
    public class NewOnChainTransactionEvent
    {
        public NewTransactionEvent NewTransactionEvent { get; set; }
        public PaymentMethodId PaymentMethodId { get; set; }

        public override string ToString()
        {
            var state = NewTransactionEvent.BlockId == null ? "Unconfirmed" : "Confirmed";
            return $"{PaymentMethodId}: New transaction {NewTransactionEvent.TransactionData.TransactionHash} ({state})";
        }
    }
}
