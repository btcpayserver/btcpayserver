using System;
using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class InvoicePaymentMethodDataModel
    {
        public bool Activated { get; set; }
        public string Destination { get; set; }
        public string PaymentLink { get; set; }

        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Rate { get; set; }

        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal PaymentMethodPaid { get; set; }

        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal TotalPaid { get; set; }

        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Due { get; set; }

        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }

        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal NetworkFee { get; set; }

        public List<Payment> Payments { get; set; }
        public string PaymentMethod { get; set; }

        public string CryptoCode { get; set; }
        public JObject AdditionalData { get; set; }

        public class Payment
        {
            public string Id { get; set; }

            [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
            public DateTime ReceivedDate { get; set; }

            [JsonConverter(typeof(NumericStringJsonConverter))]
            public decimal Value { get; set; }

            [JsonConverter(typeof(NumericStringJsonConverter))]
            public decimal Fee { get; set; }

            [JsonConverter(typeof(StringEnumConverter))]
            public PaymentStatus Status { get; set; }

            public string Destination { get; set; }

            public enum PaymentStatus
            {
                Invalid,
                Processing,
                Settled
            }
        }
    }
}
