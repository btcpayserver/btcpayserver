using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json.Linq;
using PaymentRequestData = BTCPayServer.Data.PaymentRequestData;

namespace BTCPayServer.Models.PaymentRequestViewModels
{
    public class ListPaymentRequestsViewModel : BasePagingViewModel
    {
        public List<ViewPaymentRequestViewModel> Items { get; set; }
        public override int CurrentPageCount => Items.Count;
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
            FormId = blob.FormId;
            Title = blob.Title;
            Amount = blob.Amount;
            Currency = blob.Currency;
            Description = blob.Description;
            ExpiryDate = blob.ExpiryDate?.UtcDateTime;
            Email = blob.Email;
            CustomCSSLink = blob.CustomCSSLink;
            EmbeddedCSS = blob.EmbeddedCSS;
            AllowCustomPaymentAmounts = blob.AllowCustomPaymentAmounts;
            FormResponse = blob.FormResponse is null
                ? null
                : blob.FormResponse.ToObject<Dictionary<string, object>>();
        }

        [Display(Name = "Request customer data on checkout")]
        public string FormId { get; set; }

        public bool Archived { get; set; }

        public string Id { get; set; }
        [Required] public string StoreId { get; set; }

        [Required]
        [Range(double.Epsilon, double.PositiveInfinity, ErrorMessage = "Please provide an amount greater than 0")]
        public decimal Amount { get; set; }

        [Display(Name = "Currency")]
        public string Currency { get; set; }

        [Display(Name = "Expiration Date")]
        public DateTime? ExpiryDate { get; set; }
        [Required] public string Title { get; set; }
        public string Description { get; set; }

        [Display(Name = "Store")]
        public SelectList Stores { get; set; }

        [MailboxAddress]
        public string Email { get; set; }

        [MaxLength(500)]
        [Display(Name = "Custom CSS URL")]
        public string CustomCSSLink { get; set; }

        [Display(Name = "Custom CSS Code")]
        public string EmbeddedCSS { get; set; }
        [Display(Name = "Allow payee to create invoices in their own denomination")]
        public bool AllowCustomPaymentAmounts { get; set; }

        public Dictionary<string, object> FormResponse { get; set; }
    }

    public class ViewPaymentRequestViewModel
    {
        public ViewPaymentRequestViewModel(PaymentRequestData data)
        {
            Id = data.Id;
            StoreId = data.StoreDataId;
            var blob = data.GetBlob();
            Archived = data.Archived;
            Title = blob.Title;
            Amount = blob.Amount;
            Currency = blob.Currency;
            Description = blob.Description;
            ExpiryDate = blob.ExpiryDate?.UtcDateTime;
            Email = blob.Email;
            EmbeddedCSS = blob.EmbeddedCSS;
            CustomCSSLink = blob.CustomCSSLink;
            AllowCustomPaymentAmounts = blob.AllowCustomPaymentAmounts;
            if (!string.IsNullOrEmpty(EmbeddedCSS))
                EmbeddedCSS = $"<style>{EmbeddedCSS}</style>";
            switch (data.Status)
            {
                case Client.Models.PaymentRequestData.PaymentRequestStatus.Pending:
                    Status = "Pending";
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
        public string StoreId { get; set; }
        public string Currency { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string EmbeddedCSS { get; set; }
        public string CustomCSSLink { get; set; }

#nullable enable
        public class InvoiceList : List<PaymentRequestInvoice>
        {
            static HashSet<InvoiceState> stateAllowedToDisplay = new HashSet<InvoiceState>
                {
                    new InvoiceState(InvoiceStatusLegacy.New, InvoiceExceptionStatus.None),
                    new InvoiceState(InvoiceStatusLegacy.New, InvoiceExceptionStatus.PaidPartial),
                };
            public InvoiceList()
            {

            }
            public InvoiceList(IEnumerable<PaymentRequestInvoice> collection) : base(collection)
            {

            }
            public PaymentRequestInvoice? GetReusableInvoice(decimal? amount)
            {
                return this
                    .Where(i => amount is null || amount.Value == i.Amount)
                    .FirstOrDefault(invoice => stateAllowedToDisplay.Contains(invoice.State));
            }
        }
#nullable restore
        public InvoiceList Invoices { get; set; } = new InvoiceList();
        public DateTime LastUpdated { get; set; }
        public CurrencyData CurrencyData { get; set; }
        public string AmountCollectedFormatted { get; set; }
        public string AmountFormatted { get; set; }
        public bool AnyPendingInvoice { get; set; }
        public bool PendingInvoiceHasPayments { get; set; }
        public string HubPath { get; set; }
        public bool Archived { get; set; }
        public string FormId { get; set; }
        public bool FormSubmitted { get; set; }

        public class PaymentRequestInvoice
        {
            public string Id { get; set; }
            public DateTime ExpiryDate { get; set; }
            public decimal Amount { get; set; }
            public string AmountFormatted { get; set; }
            public InvoiceState State { get; set; }
            public InvoiceStatusLegacy Status { get; set; }
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
            public string Destination { get; set; }
        }
    }
}
