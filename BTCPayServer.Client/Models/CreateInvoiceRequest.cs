using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class CreateInvoiceRequest : InvoiceDataBase
    {
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal? Amount { get; set; }
        public string[] AdditionalSearchTerms { get; set; }
        public string WebhookUrl { get; set; }
        public string WebhookSecret { get; set; }
    }
}
