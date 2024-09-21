using BTCPayServer.Payments;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Monero.Payments
{
    public class MoneroPaymentPromptDetails
    {
        public long AccountIndex { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
        public bool UseRemoteNode { get; set; }
        public string RemoteNodeProtocol { get; set; }
        public string RemoteNodeAddress { get; set; }
        public int? RemoteNodePort { get; set; }
    }
}
