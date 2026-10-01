using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.JsonConverters;
using BTCPayServer.Payments;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Models.InvoicingModels
{
    public class CheckoutModel
    {
        public class Amount
        {
            public decimal Value { get; set; }
            public string Formatted { get; set; }
        }
        public string CheckoutBodyComponentName { get; set; }
        public class AvailablePaymentMethod
        {
            [JsonConverter(typeof(PaymentMethodIdJsonConverter))]
            public PaymentMethodId PaymentMethodId { get; set; }
            public bool Displayed { get; set; }
			public string PaymentMethodName { get; set; }
			public int Order { get; set; }
            [JsonExtensionData]
            public Dictionary<string, JToken> AdditionalData { get; set; } = new();
        }
        public StoreBrandingViewModel StoreBranding { get; set; }
        public string PaymentSoundUrl { get; set; }
        public string NfcReadSoundUrl { get; set; }
        public string ErrorSoundUrl { get; set; }
        public string BrandColor { get; set; }
        public string HtmlTitle { get; set; }
        public string DefaultLang { get; set; }
        public bool ShowPayInWalletButton { get; set; }
        public bool ShowStoreHeader { get; set; }
        public bool NfcEnabled { get; set; }
        public List<AvailablePaymentMethod> AvailablePaymentMethods { get; set; } = new();
        public bool IsModal { get; set; }
        public bool IsUnsetTopUp { get; set; }
        public bool OnChainWithLnInvoiceFallback { get; set; }
        public bool CelebratePayment { get; set; }
        public string PaymentMethodCurrency { get; set; }
        public string InvoiceId { get; set; }
        public string Address { get; set; }
        public string Due { get; set; }
        [JsonIgnore]
        public string CustomerEmail { get; set; }
        public bool ShowRecommendedFee { get; set; }
        public decimal FeeRate { get; set; }
        public int ExpirationSeconds { get; set; }
        public int DisplayExpirationTimer { get; set; }
        public string Status { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string MerchantRefLink { get; set; }
        public int MaxTimeSeconds { get; set; }
        public string StoreName { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ItemDesc { get; set; }
        public string TimeLeft { get; set; }
        public string Rate { get; set; }
        public string OrderAmount { get; set; }
        public string OrderAmountFiat { get; set; }
        public Amount TaxIncluded { get; set; }
        public string InvoiceBitcoinUrl { get; set; }
        public string InvoiceBitcoinUrlQR { get; set; }
        public int TxCount { get; set; }
        public int TxCountForFee { get; set; }
        public string Paid { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string StoreSupportUrl { get; set; }
        public string CheckoutText { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string OrderId { get; set; }
        public decimal NetworkFee { get; set; }
        public int MaxTimeMinutes { get; set; }
        public string PaymentMethodId { get; set; }
        public string PaymentMethodName { get; set; }
        public string CryptoImage { get; set; }
        public string StoreId { get; set; }
        public string PeerInfo { get; set; }
        public string RootPath { get; set; }
        public bool RedirectAutomatically { get; set; }
        public bool Activated { get; set; }
        public string InvoiceCurrency { get; set; }
        public string ReceiptLink { get; set; }
        public int? RequiredConfirmations { get; set; }
        public long? ReceivedConfirmations { get; set; }
        [JsonExtensionData]
        public Dictionary<string, JToken> AdditionalData { get; set; } = new();

        /// <summary>
        /// Removes customer and merchant reference fields before serializing a public invoice status response.
        /// </summary>
        /// <remarks>
        /// Mutates this instance and its checkout and payment-method extension-data dictionaries in place,
        /// preserving the remaining checkout settings and plugin fields.
        /// </remarks>
        /// <returns>This instance after filtering.</returns>
        public CheckoutModel GetStatusResponse()
        {
            CustomerEmail = null;
            MerchantRefLink = null;
            OrderId = null;
            ItemDesc = null;
            StoreSupportUrl = null;

            foreach (var data in AvailablePaymentMethods.Select(method => method.AdditionalData).Prepend(AdditionalData))
            {
                if (data is null)
                    continue;
                foreach (var key in data.Keys.Where(IsSensitiveField).ToArray())
                    data.Remove(key);
            }

            return this;
        }

        /// <summary>
        /// Identifies extension-data fields excluded from public status responses by their case-insensitive names or prefixes.
        /// </summary>
        /// <param name="name">The extension-data field name to check.</param>
        /// <returns>Whether the name matches a buyer or customer prefix or a known metadata or merchant reference field.</returns>
        private static bool IsSensitiveField(string name) =>
            name.StartsWith("buyer", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("customer", StringComparison.OrdinalIgnoreCase) ||
            name.ToLowerInvariant() is "metadata" or "posdata" or "receiptdata" or
                "merchantreflink" or "merchantcheckoutlink" or "redirecturl" or "orderurl" or
                "orderid" or "itemdesc" or "storesupporturl";
    }
}
