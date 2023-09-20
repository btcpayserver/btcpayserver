using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Zcash.RPC.Models
{
    public partial class MakeUriRequest
    {
        [JsonProperty("address")] public string Address { get; set; }
        [JsonProperty("amount")] public long Amount { get; set; }
        [JsonProperty("payment_id")] public string PaymentId { get; set; }
        [JsonProperty("tx_description")] public string TxDescription { get; set; }
        [JsonProperty("recipient_name")] public string RecipientName { get; set; }
    }
}
