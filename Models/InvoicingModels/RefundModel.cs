using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.InvoicingModels
{
    public enum RefundSteps
    {
        SelectPaymentMethod,
        SelectRate
    }

    public class RefundModel
    {
        public string Title { get; set; }
        public SelectList AvailablePaymentMethods { get; set; }

        [Display(Name = "Select the payout method used for refund")]
        public string SelectedPayoutMethod { get; set; }
        public RefundSteps RefundStep { get; set; }
        public string SelectedRefundOption { get; set; }
        public decimal CryptoAmountNow { get; set; }
        public string CurrentRateText { get; set; }
        public decimal CryptoAmountThen { get; set; }
        public string RateThenText { get; set; }
        public string FiatText { get; set; }
        public decimal FiatAmount { get; set; }
        public decimal? OverpaidAmount { get; set; }
        public string OverpaidAmountText { get; set; }
        public decimal SubtractPercentage { get; set; }

        [Display(Name = "Specify the amount and currency for the refund")]
        public decimal CustomAmount { get; set; }
        public string CustomCurrency { get; set; }
        public string InvoiceCurrency { get; set; }
        public string CryptoCode { get; set; }
        public int CryptoDivisibility { get; set; }
        public int InvoiceDivisibility { get; set; }
    }
}
