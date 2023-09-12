using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public class PaymentRequestData : PaymentRequestBaseData
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public PaymentRequestStatus Status { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset CreatedTime { get; set; }
        public string Id { get; set; }
        public bool Archived { get; set; }
        public enum PaymentRequestStatus
        {
            Pending = 0,
            Completed = 1,
            Expired = 2,
            Processing = 3
        }
    }
}
