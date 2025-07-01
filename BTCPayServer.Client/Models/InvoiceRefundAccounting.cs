namespace BTCPayServer.Client.Models
{
    public class InvoiceRefundAccounting
    {
        public decimal CryptoAmountNow { get; set; } = 0m;
        public string CurrentRateText { get; set; }
        public decimal CryptoAmountThen { get; set; } = 0m;
        public decimal FiatAmount { get; set; } = 0m;
        public decimal? OverpaidAmount { get; set; } = null;
        public string InvoiceCurrency { get; set; } = string.Empty;
        public string CryptoCode { get; set; } = string.Empty;
        public int CryptoDivisibility { get; set; }
        public int InvoiceDivisibility { get; set; }
    }
}
