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
        [Display(Name = "Select the payment method used for refund")]
        public string SelectedPaymentMethod { get; set; }
        public RefundSteps RefundStep { get; set; }
        public string SelectedRefundOption { get; set; }
        public decimal CryptoAmountNow { get; set; }
        public string CurrentRateText { get; set; }
        public decimal CryptoAmountThen { get; set; }
        public string RateThenText { get; set; }
        public string FiatText { get; set; }
        public decimal FiatAmount { get; set; }
    }
}
