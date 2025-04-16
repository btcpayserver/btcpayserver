using System;
using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public enum PaymentRequestStatus
    {
        /// <summary>
        /// Not enough has been paid or settled
        /// </summary>
        Pending,
        /// <summary>
        /// Paid and fully settled
        /// </summary>
        Completed,
        /// <summary>
        /// Expired before full settlement
        /// </summary>
        Expired,
        /// <summary>
        /// Paid enough, awaiting full settlement
        /// </summary>
        Processing,
    }
    public class PaymentRequestBaseData
    {
        public string StoreId { get; set; }
        [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? ExpiryDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Email { get; set; }
        /// <summary>
        /// Linking to invoices outside BTCPay Server using & user defined ids
        /// </summary>
        public string ReferenceId { get; set; }
        public bool AllowCustomPaymentAmounts { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public PaymentRequestStatus Status { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset CreatedTime { get; set; }
        public string Id { get; set; }
        public bool Archived { get; set; }

        public string FormId { get; set; }
        public JObject FormResponse { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; }
    }
}
