using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class WebhookEvent
    {
        public string DeliveryId { get; set; }
        public string OrignalDeliveryId { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public WebhookEventType Type { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset Timestamp { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; }
    }
}
