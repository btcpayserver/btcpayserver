using System;
using System.Collections.Generic;
using NBitpayClient;
using Newtonsoft.Json;

namespace BTCPayServer.Models
{
    public class BitpayCreateInvoiceRequest
    {
        [JsonProperty(PropertyName = "buyer")]
        public Buyer Buyer { get; set; }
        [JsonProperty(PropertyName = "buyerEmail", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string BuyerEmail { get; set; }
        [JsonProperty(PropertyName = "buyerCountry", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string BuyerCountry { get; set; }
        [JsonProperty(PropertyName = "buyerZip", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string BuyerZip { get; set; }
        [JsonProperty(PropertyName = "buyerState", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string BuyerState { get; set; }
        [JsonProperty(PropertyName = "buyerCity", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string BuyerCity { get; set; }
        [JsonProperty(PropertyName = "buyerAddress2", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string BuyerAddress2 { get; set; }
        [JsonProperty(PropertyName = "buyerAddress1", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string BuyerAddress1 { get; set; }
        [JsonProperty(PropertyName = "buyerName", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string BuyerName { get; set; }
        [JsonProperty(PropertyName = "physical", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Physical { get; set; }
        [JsonProperty(PropertyName = "redirectURL", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string RedirectURL { get; set; }
        [JsonProperty(PropertyName = "notificationURL", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string NotificationURL { get; set; }
        [JsonProperty(PropertyName = "extendedNotifications", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool ExtendedNotifications { get; set; }
        [JsonProperty(PropertyName = "fullNotifications", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool FullNotifications { get; set; }
        [JsonProperty(PropertyName = "transactionSpeed", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string TransactionSpeed { get; set; }
        [JsonProperty(PropertyName = "buyerPhone", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string BuyerPhone { get; set; }
        [JsonProperty(PropertyName = "posData", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string PosData { get; set; }
        [JsonProperty(PropertyName = "itemCode", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ItemCode { get; set; }
        [JsonProperty(PropertyName = "itemDesc", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ItemDesc { get; set; }
        [JsonProperty(PropertyName = "orderId", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string OrderId { get; set; }
        [JsonProperty(PropertyName = "currency", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Currency { get; set; }
        [JsonProperty(PropertyName = "price", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public decimal? Price { get; set; }
        [JsonProperty(PropertyName = "defaultPaymentMethod", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string DefaultPaymentMethod { get; set; }
        [JsonProperty(PropertyName = "notificationEmail", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string NotificationEmail { get; set; }
        [JsonConverter(typeof(DateTimeJsonConverter))]
        [JsonProperty(PropertyName = "expirationTime", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public DateTimeOffset? ExpirationTime { get; set; }
        [JsonProperty(PropertyName = "status", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Status { get; set; }
        [JsonProperty(PropertyName = "minerFees", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, MinerFeeInfo> MinerFees { get; set; }
        [JsonProperty(PropertyName = "supportedTransactionCurrencies", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, InvoiceSupportedTransactionCurrency> SupportedTransactionCurrencies { get; set; }
        [JsonProperty(PropertyName = "exchangeRates", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, decimal>> ExchangeRates { get; set; }
        [JsonProperty(PropertyName = "refundable", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Refundable { get; set; }
        [JsonProperty(PropertyName = "taxIncluded", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public decimal? TaxIncluded { get; set; }
        [JsonProperty(PropertyName = "nonce", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public long Nonce { get; set; }
        [JsonProperty(PropertyName = "guid", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Guid { get; set; }
        [JsonProperty(PropertyName = "token", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Token { get; set; }

        [JsonProperty(PropertyName = "redirectAutomatically", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool? RedirectAutomatically { get; set; }
        [JsonProperty(PropertyName = "requiresRefundEmail", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool? RequiresRefundEmail { get; set; }

        //Bitpay compatibility: create invoice in btcpay uses this instead of supportedTransactionCurrencies
        [JsonProperty(PropertyName = "paymentCurrencies", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public IEnumerable<string> PaymentCurrencies { get; set; }
    }
}
