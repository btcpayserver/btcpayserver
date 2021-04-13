using System.Collections.Generic;

namespace BTCPayServer.Models.InvoicingModels
{
    public class CheckoutUIPaymentMethodSettings
    {
        public string ExtensionPartial { get; set; }
        public string CheckoutBodyVueComponentName { get; set; }
        public string CheckoutHeaderVueComponentName { get; set; }
        public string NoScriptPartialName { get; set; }
    }
    public class PaymentModel
    {
        public CheckoutUIPaymentMethodSettings UISettings;
        public class AvailableCrypto
        {
            public string PaymentMethodId { get; set; }
            public string CryptoImage { get; set; }
            public string Link { get; set; }
            public string PaymentMethodName { get; set; }
            public bool IsLightning { get; set; }
            public string CryptoCode { get; set; }
        }
        public string CustomCSSLink { get; set; }
        public string CustomLogoLink { get; set; }
        public string HtmlTitle { get; set; }
        public string DefaultLang { get; set; }
        public List<AvailableCrypto> AvailableCryptos { get; set; } = new List<AvailableCrypto>();
        public bool IsModal { get; set; }
        public string CryptoCode { get; set; }
        public string InvoiceId { get; set; }
        public string BtcAddress { get; set; }
        public string BtcDue { get; set; }
        public string CustomerEmail { get; set; }
        public bool RequiresRefundEmail { get; set; }
        public bool ShowRecommendedFee { get; set; }
        public decimal FeeRate { get; set; }
        public int ExpirationSeconds { get; set; }
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
        public string BtcPaid { get; set; }
        public string StoreEmail { get; set; }

        public string OrderId { get; set; }
        public decimal NetworkFee { get; set; }
        public bool IsMultiCurrency { get; set; }
        public int MaxTimeMinutes { get; set; }
        public string PaymentType { get; set; }
        public string PaymentMethodId { get; set; }
        public string PaymentMethodName { get; set; }
        public string CryptoImage { get; set; }
        public string StoreId { get; set; }
        public string PeerInfo { get; set; }
        public string RootPath { get; set; }
        public bool RedirectAutomatically { get; set; }
        public bool Activated { get; set; }
        public string InvoiceCurrency { get; set; }
    }
}
