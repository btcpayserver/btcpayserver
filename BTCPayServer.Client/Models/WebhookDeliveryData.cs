using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class WebhookDeliveryData
    {
        public string Id { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset Timestamp { get; set; }
        public int? HttpCode { get; set; }
        public string ErrorMessage { get; set; }
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public WebhookDeliveryStatus Status { get; set; }
    }
}
