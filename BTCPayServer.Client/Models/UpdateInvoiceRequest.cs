using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class UpdateInvoiceRequest
    {
        public JObject Metadata { get; set; }
    }
}
