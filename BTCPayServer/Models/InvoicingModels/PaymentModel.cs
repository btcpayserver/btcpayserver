using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;

namespace BTCPayServer.Models.InvoicingModels
{
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
            public bool Displayed { get; set; }
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
        public List<AvailableCrypto> AvailableCryptos { get; set; } = new();
        public bool IsModal { get; set; }
        public bool IsUnsetTopUp { get; set; }
        public bool OnChainWithLnInvoiceFallback { get; set; }
        public bool CelebratePayment { get; set; }
        public string CryptoCode { get; set; }
        public string InvoiceId { get; set; }
        public string BtcAddress { get; set; }
        public string BtcDue { get; set; }
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
        public string BtcPaid { get; set; }
        public string StoreSupportUrl { get; set; }

        public string OrderId { get; set; }
        public decimal NetworkFee { get; set; }
        public bool IsMultiCurrency { get; set; }
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

        public HashSet<string> ExtensionPartials { get; } = new HashSet<string>();
    }
}
