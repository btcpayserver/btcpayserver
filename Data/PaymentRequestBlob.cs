using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace BTCPayServer.Data
{
    public class PaymentRequestBlob
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Email { get; set; }
        public bool AllowCustomPaymentAmounts { get; set; }

        public string FormId { get; set; }

        public JObject FormResponse { get; set; }
        public string RequestBaseUrl { get; set; }
    }
}
