using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Models
{
    class DateTimeJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(DateTimeOffset);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var v = (long)reader.Value;
            Check(v);
            return unixRef + TimeSpan.FromMilliseconds((long)v);
        }

        static DateTimeOffset unixRef = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var date = ((DateTimeOffset)value).ToUniversalTime();
            long v = (long)(date - unixRef).TotalMilliseconds;
            Check(v);
            writer.WriteValue(v);
        }

        private static void Check(long v)
        {
            if (v < 0)
                throw new FormatException("Invalid datetime (less than 1/1/1970)");
        }
    }

    //{"facade":"pos/invoice","data":{,}}
    public class InvoiceResponse
    {
        //"url":"https://test.bitpay.com/invoice?id=9saCHtp1zyPcNoi3rDdBu8"
        [JsonProperty("url")]
        public string Url
        {
            get; set;
        }
        //"posData":"posData"
        [JsonProperty("posData")]
        public string PosData
        {
            get; set;
        }
        //status":"new"
        [JsonProperty("status")]
        public string Status
        {
            get; set;
        }
        //"btcPrice":"0.001157"
        [JsonProperty("btcPrice")]
        [Obsolete("Use CryptoInfo.Price instead")]
        public string BTCPrice
        {
            get; set;
        }

        //"btcDue":"0.001160"
        [JsonProperty("btcDue")]
        [Obsolete("Use CryptoInfo.Due instead")]
        public string BTCDue
        {
            get; set;
        }

        [JsonProperty("cryptoInfo")]
        public List<NBitpayClient.InvoiceCryptoInfo> CryptoInfo { get; set; }

        //"price":5
        [JsonProperty("price")]
        public decimal Price
        {
            get; set;
        }

        //"currency":"USD"
        [JsonProperty("currency")]
        public string Currency
        {
            get; set;
        }

        //"exRates":{"USD":4320.02}
        [JsonProperty("exRates")]
        [Obsolete("Use CryptoInfo.ExRates instead")]
        public Dictionary<string, decimal> ExRates
        {
            get; set;
        }

        //"buyerTotalBtcAmount":"0.001160"
        [JsonProperty("buyerTotalBtcAmount")]
        public string BuyerTotalBtcAmount
        {
            get; set;
        }

        //"itemDesc":"Some description"
        [JsonProperty("itemDesc")]
        public string ItemDesc
        {
            get; set;
        }

        //"orderId":"orderId"
        [JsonProperty("orderId")]
        public string OrderId
        {
            get; set;
        }

        //"guid":"e238ce2a-06da-47e9-aefd-2588d4aa5f8d"
        [JsonProperty("guid")]
        public string Guid
        {
            get; set;
        }
        //"id":"9saCHtp1zyPcNoi3rDdBu8"
        [JsonProperty("id")]
        public string Id
        {
            get; set;
        }

        [JsonConverter(typeof(DateTimeJsonConverter))]
        [JsonProperty("invoiceTime")]
        public DateTimeOffset InvoiceTime
        {
            get; set;
        }

        [JsonConverter(typeof(DateTimeJsonConverter))]
        [JsonProperty("expirationTime")]
        public DateTimeOffset ExpirationTime
        {
            get; set;
        }

        [JsonConverter(typeof(DateTimeJsonConverter))]
        [JsonProperty("currentTime")]
        public DateTimeOffset CurrentTime
        {
            get; set;
        }

        //"lowFeeDetected":false
        [JsonProperty("lowFeeDetected")]
        public bool LowFeeDetected
        {
            get; set;
        }

        //"btcPaid":"0.000000"
        [JsonProperty("btcPaid")]
        [Obsolete("Use CryptoInfo.Paid instead")]
        public string BTCPaid
        {
            get; set;
        }

        //"rate":4320.02
        [JsonProperty("rate")]
        [Obsolete("Use CryptoInfo.Rate instead")]
        public decimal Rate
        {
            get; set;
        }

        //"exceptionStatus":false
        //Can be `paidPartial`, `paidOver`, or false
        [JsonProperty("exceptionStatus")]
        public JToken ExceptionStatus
        {
            get; set;
        }

        //"paymentUrls":{"BIP21":"bitcoin:muFQCEbfRJohcds3bkfv1sRFj8uVTfv2wv?amount=0.001160","BIP72":"bitcoin:muFQCEbfRJohcds3bkfv1sRFj8uVTfv2wv?amount=0.001160&r=https://test.bitpay.com/i/9saCHtp1zyPcNoi3rDdBu8","BIP72b":"bitcoin:?r=https://test.bitpay.com/i/9saCHtp1zyPcNoi3rDdBu8","BIP73":"https://test.bitpay.com/i/9saCHtp1zyPcNoi3rDdBu8"}
        [JsonProperty("paymentUrls")]
        [Obsolete("Use CryptoInfo.PaymentsUrls instead")]
        public NBitpayClient.InvoicePaymentUrls PaymentUrls
        {
            get; set;
        }
        //"refundAddressRequestPending":false
        [JsonProperty("refundAddressRequestPending")]
        public bool RefundAddressRequestPending
        {
            get; set;
        }
        //"buyerPaidBtcMinerFee":"0.000003"
        [JsonProperty("buyerPaidBtcMinerFee")]
        public string BuyerPaidBtcMinerFee
        {
            get; set;
        }

        //"bitcoinAddress":"muFQCEbfRJohcds3bkfv1sRFj8uVTfv2wv"
        [JsonProperty("bitcoinAddress")]
        [Obsolete("Use CryptoInfo.Address instead")]
        public string BitcoinAddress
        {
            get; set;
        }
        //"token":"9jF3TU7A8inKHDRQXFrKcRnMkLXWGQ2yKf7pnjMKGHEfpwTNV35HytrD9FXDBy25Li"
        [JsonProperty("token")]
        public string Token
        {
            get; set;
        }

        [JsonProperty("flags")]
        public Flags Flags
        {
            get; set;
        }

        [JsonProperty("paymentSubtotals")]
        public Dictionary<string, long> PaymentSubtotals { get; set; }

        [JsonProperty("paymentTotals")]
        public Dictionary<string, long> PaymentTotals { get; set; }

        [JsonProperty("amountPaid", DefaultValueHandling = DefaultValueHandling.Include)]
        public long AmountPaid { get; set; }

        [JsonProperty("minerFees")]
        public Dictionary<string, NBitpayClient.MinerFeeInfo> MinerFees { get; set; }

        [JsonProperty("exchangeRates")]
        public Dictionary<string, Dictionary<string, decimal>> ExchangeRates { get; set; }

        [JsonProperty("supportedTransactionCurrencies")]
        public Dictionary<string, NBitpayClient.InvoiceSupportedTransactionCurrency> SupportedTransactionCurrencies { get; set; }

        [JsonProperty("addresses")]
        public Dictionary<string, string> Addresses { get; set; }
        [JsonProperty("paymentCodes")]
        public Dictionary<string, NBitpayClient.InvoicePaymentUrls> PaymentCodes { get; set; }
        [JsonProperty("buyer")]
        public JObject Buyer { get; set; }
    }
    public class Flags
    {
        [JsonProperty("refundable")]
        public bool Refundable
        {
            get; set;
        }
    }

}
