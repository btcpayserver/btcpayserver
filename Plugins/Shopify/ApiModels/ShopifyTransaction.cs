using System;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Shopify.ApiModels
{
    public class ShopifyTransaction
    {
        [JsonProperty("amount")]
        public decimal? Amount { get; set; }

        [JsonProperty("authorization")]
        public string Authorization { get; set; }

        [JsonProperty("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonProperty("device_id")]
        public string DeviceId { get; set; }

        [JsonProperty("gateway")]
        public string Gateway { get; set; }
        [JsonProperty("kind")]
        public string Kind { get; set; }
        [JsonProperty("order_id")]
        public long? OrderId { get; set; }

        /// <summary>
        /// A standardized error code, e.g. 'incorrect_number', independent of the payment provider. Value can be null. A full list of known values can be found at https://help.shopify.com/api/reference/transaction.
        /// </summary>
        [JsonProperty("error_code")]
        public string ErrorCode { get; set; }

        /// <summary>
        /// The status of the transaction. Valid values are: pending, failure, success or error.
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }
        [JsonProperty("test")]
        public bool? Test { get; set; }
        [JsonProperty("currency")]
        public string Currency { get; set; }
        /// <summary>
        /// This property is undocumented by Shopify.
        /// </summary>
        [JsonProperty("parent_id")]
        public long? ParentId { get; set; }
    }
}
