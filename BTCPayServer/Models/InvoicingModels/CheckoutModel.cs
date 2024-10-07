using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.JsonConverters;
using BTCPayServer.Payments;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Models.InvoicingModels
{
    public class CheckoutModel
    {
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
        public List<AvailablePaymentMethod> AvailablePaymentMethods { get; set; } = new();
        public bool IsModal { get; set; }
        public bool IsUnsetTopUp { get; set; }
        public bool OnChainWithLnInvoiceFallback { get; set; }
        public bool CelebratePayment { get; set; }
        public string PaymentMethodCurrency { get; set; }
        public string InvoiceId { get; set; }
        public string Address { get; set; }
        public string Due { get; set; }
        public string CustomerEmail { get; set; }
        public bool ShowRecommendedFee { get; set; }
        public decimal FeeRate { get; set; }
        public int ExpirationSeconds { get; set; }
        public int DisplayExpirationTimer { get; set; }
        public string Status { get; set; }
        public string MerchantRefLink { get; set; }
        public int MaxTimeSeconds { get; set; }
        public string StoreName { get; set; }
        public string ItemDesc { get; set; }
        public string TimeLeft { get; set; }
        public string Rate { get; set; }
        public string OrderAmount { get; set; }
        public string OrderAmountFiat { get; set; }
        public string InvoiceBitcoinUrl { get; set; }
        public string InvoiceBitcoinUrlQR { get; set; }
        public int TxCount { get; set; }
        public int TxCountForFee { get; set; }
        public string Paid { get; set; }
        public string StoreSupportUrl { get; set; }

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
    }
}
