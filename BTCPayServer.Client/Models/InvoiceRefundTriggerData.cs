namespace BTCPayServer.Client.Models
{
    public class InvoiceRefundTriggerData
    {
        public decimal PaymentAmountNow { get; set; } = 0m;
        public string CurrentRateText { get; set; }
        public decimal PaymentAmountThen { get; set; } = 0m;
        public decimal InvoiceAmount { get; set; } = 0m;
        public decimal? OverpaidPaymentAmount { get; set; } = null;
        public string InvoiceCurrency { get; set; } = string.Empty;
        public string PaymentCurrency { get; set; } = string.Empty;
        public int PaymentCurrencyDivisibility { get; set; }
        public int InvoiceCurrencyDivisibility { get; set; }
    }
}
