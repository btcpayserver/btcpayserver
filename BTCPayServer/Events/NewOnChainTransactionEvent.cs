using NBXplorer.Models;

namespace BTCPayServer.Events
{
    public class NewOnChainTransactionEvent
    {
        public NewTransactionEvent NewTransactionEvent { get; set; }
        public string CryptoCode { get; set; }

        public override string ToString()
        {
            var state = NewTransactionEvent.BlockId == null ? "Unconfirmed" : "Confirmed";
            return $"{CryptoCode}: New transaction {NewTransactionEvent.TransactionData.TransactionHash} ({state})";
        }
    }
}
