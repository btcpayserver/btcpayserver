using System;
using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public class InvoiceData : CreateInvoiceRequest
    {
        public string Id { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public InvoiceStatus Status { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public InvoiceExceptionStatus ExceptionStatus { get; set; }
        public Dictionary<string, PaymentMethodDataModel> PaymentMethodData { get; set; }
        
        public class PaymentMethodDataModel
        {
            public string Destination { get; set; }
            public string PaymentLink { get; set; }

            [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
            public decimal Rate { get; set; }

            [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
            public decimal PaymentMethodPaid { get; set; }

            [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
            public decimal TotalPaid { get; set; }

            [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
            public decimal Due { get; set; }

            [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
            public decimal Amount { get; set; }

            [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
            public decimal NetworkFee { get; set; }

            public List<Payment> Payments { get; set; }
        
            public class Payment
            {
                public string Id { get; set; }

                [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
                public DateTime ReceivedDate { get; set; }

                [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
                public decimal Value { get; set; }

                [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
                public decimal Fee { get; set; }

                [JsonConverter(typeof(StringEnumConverter))]
                public PaymentStatus Status { get; set; }

                public string Destination { get; set; }

                public enum PaymentStatus
                {
                    Invalid,
                    AwaitingConfirmation,
                    AwaitingCompletion,
                    Complete
                }
            }
        }

    }
}
