namespace BTCPayServer.Client.Models
{
    public class PayLightningInvoiceRequest
    {
        [Newtonsoft.Json.JsonProperty("BOLT11")]
        public string BOLT11 { get; set; }
    }
}
