﻿using System;
using System.Collections.Generic;
using System.Text;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public enum PayoutState
    {
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
        [JsonConverter(typeof(DecimalStringJsonConverter))]
        public decimal Amount { get; set; }
        [JsonConverter(typeof(DecimalStringJsonConverter))]
        public decimal PaymentMethodAmount { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public PayoutState State { get; set; }
    }
}
