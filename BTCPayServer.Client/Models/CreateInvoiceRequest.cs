using System;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class CreateInvoiceRequest : InvoiceDataBase
    {
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal? Amount { get; set; }
        public string[] AdditionalSearchTerms { get; set; }
    }
}
