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
        public readonly static JsonSerializerSettings DefaultSerializerSettings;
        static WebhookEvent()
        {
            DefaultSerializerSettings = new JsonSerializerSettings();
            DefaultSerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
            NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(DefaultSerializerSettings);
            DefaultSerializerSettings.Formatting = Formatting.None;
        }
        public string DeliveryId { get; set; }
        public string WebhookId { get; set; }
        public string OrignalDeliveryId { get; set; }
        public bool IsRedelivery { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public WebhookEventType Type { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset Timestamp { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; }
        public T ReadAs<T>()
        {
            var str = JsonConvert.SerializeObject(this, DefaultSerializerSettings);
            return JsonConvert.DeserializeObject<T>(str, DefaultSerializerSettings);
        }
    }
}
