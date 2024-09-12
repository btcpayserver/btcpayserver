using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Models.PaymentRequestViewModels;
using static BTCPayServer.Client.Models.InvoiceDataBase;

namespace BTCPayServer.Models.InvoicingModels
{
    public class InvoiceReceiptViewModel
    {
        public InvoiceStatus Status { get; set; }
        public StoreBrandingViewModel StoreBranding { get; set; }
        public string InvoiceId { get; set; }
        public string OrderId { get; set; }
        public string Currency { get; set; }
        public string StoreName { get; set; }
        public decimal Amount { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; }
        public Dictionary<string, object> CartData { get; set; }
        public ReceiptOptions ReceiptOptions { get; set; }
        public List<ViewPaymentRequestViewModel.PaymentRequestInvoicePayment> Payments { get; set; }
        public string RedirectUrl { get; set; }
    }
}
