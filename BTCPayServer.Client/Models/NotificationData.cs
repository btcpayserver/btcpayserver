using System;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class NotificationData
    {
        public string Id { get; set; }
        public string Identifier { get; set; }
        public string Type { get; set; }
        public string Body { get; set; }
        public bool Seen { get; set; }
        public Uri Link { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset CreatedTime { get; set; }
    }
}
