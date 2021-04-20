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
        string _OriginalDeliveryId;
        public string OriginalDeliveryId
        {
            get
            {
                if (_OriginalDeliveryId is null)
                {
                    // Due to a typo in old version, we serialized `orignalDeliveryId` rather than `orignalDeliveryId`
                    // We silently fix that here.
                    // Note we can remove this code later on, as old webhook event are unlikely to be useful to anyone,
                    // and having a null orignalDeliveryId is not end of the world
                    if (AdditionalData != null &&
                        AdditionalData.TryGetValue("orignalDeliveryId", out var tok))
                    {
                        _OriginalDeliveryId = tok.Value<string>();
                        AdditionalData.Remove("orignalDeliveryId");
                    }
                }
                return _OriginalDeliveryId;
            }
            set
            {
                _OriginalDeliveryId = value;
            }
        }
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
