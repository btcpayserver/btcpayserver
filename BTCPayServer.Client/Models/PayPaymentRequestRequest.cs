using System;
using System.Collections.Generic;
using System.Text;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class PayPaymentRequestRequest
    {
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal? Amount { get; set; }
        public bool? AllowPendingInvoiceReuse { get; set; }
    }
}
