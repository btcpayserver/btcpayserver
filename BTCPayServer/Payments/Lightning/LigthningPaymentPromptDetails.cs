using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Lightning;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Lightning
{
    public class LigthningPaymentPromptDetails
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 PaymentHash { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 Preimage { get; set; }
        /// <summary>
        /// The invoice id for the lightning node
        /// </summary>
        public string InvoiceId { get; set; }
        public string NodeInfo { get; set; }
    }
}
