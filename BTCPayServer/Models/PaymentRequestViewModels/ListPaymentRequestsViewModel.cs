using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Mvc.Rendering;
using PaymentRequestData = BTCPayServer.Data.PaymentRequestData;

namespace BTCPayServer.Models.PaymentRequestViewModels
{
    public class ListPaymentRequestsViewModel : BasePagingViewModel
    {
        public List<ViewPaymentRequestViewModel> Items { get; set; }

    }

    public class UpdatePaymentRequestViewModel
    {
        public UpdatePaymentRequestViewModel()
        {
        }

        public UpdatePaymentRequestViewModel(PaymentRequestData data)
        {
            if (data == null)
            {
                return;
            }

            Id = data.Id;
            StoreId = data.StoreDataId;
            Archived = data.Archived;
            var blob = data.GetBlob();
            Title = blob.Title;
            Amount = blob.Amount;
            Currency = blob.Currency;
            Description = blob.Description;
            ExpiryDate = blob.ExpiryDate;
            Email = blob.Email;
            CustomCSSLink = blob.CustomCSSLink;
            EmbeddedCSS = blob.EmbeddedCSS;
            AllowCustomPaymentAmounts = blob.AllowCustomPaymentAmounts;
        }

        public bool Archived { get; set; }

        public string Id { get; set; }
        [Required] public string StoreId { get; set; }

        [Required]
        [Range(double.Epsilon, double.PositiveInfinity, ErrorMessage = "Please provide an amount greater than 0")]
        public decimal Amount { get; set; }

        [Display(Name = "The currency used for payment request. (e.g. BTC, LTC, USD, etc.)")]
        public string Currency { get; set; }

        [Display(Name = "Expiration Date")]
        public DateTime? ExpiryDate { get; set; }
        [Required] public string Title { get; set; }
        public string Description { get; set; }

        public SelectList Stores { get; set; }
        [EmailAddress]
        public string Email { get; set; }

        [MaxLength(500)]
        [Display(Name = "Custom bootstrap CSS file")]
        public string CustomCSSLink { get; set; }

        [Display(Name = "Custom CSS Code")]
        public string EmbeddedCSS { get; set; }
        [Display(Name = "Allow payee to create invoices in their own denomination")]
        public bool AllowCustomPaymentAmounts { get; set; }
    }

    public class ViewPaymentRequestViewModel
    {
        public ViewPaymentRequestViewModel(PaymentRequestData data)
        {
            Id = data.Id;
            var blob = data.GetBlob();
            Archived = data.Archived;
            Title = blob.Title;
            Amount = blob.Amount;
            Currency = blob.Currency;
            Description = blob.Description;
            ExpiryDate = blob.ExpiryDate;
            Email = blob.Email;
            EmbeddedCSS = blob.EmbeddedCSS;
            CustomCSSLink = blob.CustomCSSLink;
            AllowCustomPaymentAmounts = blob.AllowCustomPaymentAmounts;
            if (!string.IsNullOrEmpty(EmbeddedCSS))
                EmbeddedCSS = $"<style>{EmbeddedCSS}</style>";
            switch (data.Status)
            {
                case Client.Models.PaymentRequestData.PaymentRequestStatus.Pending:
                    Status = ExpiryDate.HasValue ? $"Expires on {ExpiryDate.Value:g}" : "Pending";
                    IsPending = true;
                    break;
                case Client.Models.PaymentRequestData.PaymentRequestStatus.Completed:
                    Status = "Settled";
                    break;
                case Client.Models.PaymentRequestData.PaymentRequestStatus.Expired:
                    Status = "Expired";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public bool AllowCustomPaymentAmounts { get; set; }
        public string Email { get; set; }
        public string Status { get; set; }
        public bool IsPending { get; set; }
        public decimal AmountCollected { get; set; }
        public decimal AmountDue { get; set; }
        public string AmountDueFormatted { get; set; }
        public decimal Amount { get; set; }
        public string Id { get; set; }
        public string Currency { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string EmbeddedCSS { get; set; }
        public string CustomCSSLink { get; set; }
        public List<PaymentRequestInvoice> Invoices { get; set; } = new List<PaymentRequestInvoice>();
        public DateTime LastUpdated { get; set; }
        public CurrencyData CurrencyData { get; set; }
        public string AmountCollectedFormatted { get; set; }
        public string AmountFormatted { get; set; }
        public bool AnyPendingInvoice { get; set; }
        public bool PendingInvoiceHasPayments { get; set; }
        public string HubPath { get; set; }
        public bool Archived { get; set; }

        public class PaymentRequestInvoice
        {
            public string Id { get; set; }
            public DateTime ExpiryDate { get; set; }
            public decimal Amount { get; set; }
            public string AmountFormatted { get; set; }
            public InvoiceState State { get; set; }
            public InvoiceStatus Status { get; set; }
            public string StateFormatted { get; set; }

            public List<PaymentRequestInvoicePayment> Payments { get; set; }
            public string Currency { get; set; }
        }

        public class PaymentRequestInvoicePayment
        {
            public string PaymentMethod { get; set; }
            public decimal Amount { get; set; }
            public string RateFormatted { get; set; }
            public decimal Paid { get; set; }
            public string PaidFormatted { get; set; }
            public DateTime ReceivedDate { get; set; }
            public string Link { get; set; }
            public string Id { get; set; }
        }
    }
}
