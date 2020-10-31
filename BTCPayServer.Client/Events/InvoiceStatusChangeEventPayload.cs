using BTCPayServer.Client.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Events
{
    public class InvoiceStatusChangeEventPayload
    {
        public const string EventType = "invoice_status";
        public string InvoiceId { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public InvoiceStatus Status { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public InvoiceExceptionStatus AdditionalStatus { get; set; }
    }
}
