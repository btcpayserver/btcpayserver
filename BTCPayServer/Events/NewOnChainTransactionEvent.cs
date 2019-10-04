using NBXplorer.Models;

namespace BTCPayServer.Events
{
    public class NewOnChainTransactionEvent
    {
        public NewTransactionEvent NewTransactionEvent { get; set; }
        public string CryptoCode { get; set; }
    }
}
