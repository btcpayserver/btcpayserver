using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public class MarkInvoiceStatusRequest
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public InvoiceStatus Status { get; set; }
    }
}
