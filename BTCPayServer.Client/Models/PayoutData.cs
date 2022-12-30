using System;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public enum PayoutState
    {
        AwaitingApproval,
        AwaitingPayment,
        InProgress,
        Completed,
        Cancelled
    }
    public class PayoutData
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset Date { get; set; }
        public string Id { get; set; }
        public string PullPaymentId { get; set; }
        public string Destination { get; set; }
        public string PaymentMethod { get; set; }
        public string CryptoCode { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal? PaymentMethodAmount { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public PayoutState State { get; set; }
        public int Revision { get; set; }
        public JObject PaymentProof { get; set; }
    }
}
