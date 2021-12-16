using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Shopify.ApiModels
{
    public class ShopifyOrder
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("total_price")]
        public decimal TotalPrice { get; set; }
        [JsonProperty("total_outstanding")]
        public decimal TotalOutstanding { get; set; }
        [JsonProperty("currency")]
        public string Currency { get; set; }
        [JsonProperty("presentment_currency")]
        public string PresentmentCurrency { get; set; }
        [JsonProperty("financial_status")]
        public string FinancialStatus { get; set; }
        [JsonProperty("transactions")]
        public IEnumerable<ShopifyTransaction> Transactions { get; set; }
    }
}
