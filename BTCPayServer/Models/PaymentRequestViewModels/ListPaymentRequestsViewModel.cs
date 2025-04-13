using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services;
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
        
        public SearchString Search { get; set; }
        public string SearchText { get; set; }
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
            Amount = data.Amount;
            Currency = data.Currency;
            Description = blob.Description;
            ExpiryDate = data.Expiry?.UtcDateTime;
            Email = blob.Email;
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
        
        [Required]
        public string Title { get; set; }
        
        [Display(Name = "Memo")]
        public string Description { get; set; }

        [Display(Name = "Store")]
        public SelectList Stores { get; set; }

        [MailboxAddress]
        public string Email { get; set; }
        
        [Display(Name = "Allow payee to create invoices with custom amounts")]
        public bool AllowCustomPaymentAmounts { get; set; }

        public Dictionary<string, object> FormResponse { get; set; }
        public bool AmountAndCurrencyEditable { get; set; } = true;
        public bool? HasEmailRules { get; set; }
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
            Amount = data.Amount;
            Currency = data.Currency;
            Description = blob.Description;
            ExpiryDate = data.Expiry?.UtcDateTime;
            Email = blob.Email;
            AllowCustomPaymentAmounts = blob.AllowCustomPaymentAmounts;
            switch (data.Status)
            {
                case Client.Models.PaymentRequestStatus.Pending:
                    Status = "Pending";
                    IsPending = true;
                    break;
                case Client.Models.PaymentRequestStatus.Processing:
                    Status = "Processing";
                    break;
                case Client.Models.PaymentRequestStatus.Completed:
                    Status = "Settled";
                    break;
                case Client.Models.PaymentRequestStatus.Expired:
                    Status = "Expired";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        public StoreBrandingViewModel StoreBranding { get; set; }
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
        public string StoreName { get; set; }
        public string StoreWebsite { get; set; }

#nullable enable
        public class InvoiceList : List<PaymentRequestInvoice>
        {
            static HashSet<InvoiceState> stateAllowedToDisplay = new HashSet<InvoiceState>
                {
                    new InvoiceState(InvoiceStatus.New, InvoiceExceptionStatus.None),
                    new InvoiceState(InvoiceStatus.New, InvoiceExceptionStatus.PaidPartial),
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
            public string StateFormatted { get; set; }

            public List<PaymentRequestInvoicePayment> Payments { get; set; }
            public string Currency { get; set; }
        }

        public class PaymentRequestInvoicePayment
        {
            public static List<ViewPaymentRequestViewModel.PaymentRequestInvoicePayment>
                GetViewModels(
                InvoiceEntity invoice,
                DisplayFormatter displayFormatter,
                TransactionLinkProviders txLinkProvider,
                PaymentMethodHandlerDictionary handlers)
            {
                return invoice
                .GetPayments(true)
                .Select(paymentEntity =>
                {
                    var paymentMethodId = paymentEntity.PaymentMethodId;
                    if (paymentMethodId is null)
                    {
                        return null;
                    }
                    string txId = paymentEntity.Id;

                    string link = paymentMethodId is null ? null : txLinkProvider.GetTransactionLink(paymentMethodId, txId);

                    return new ViewPaymentRequestViewModel.PaymentRequestInvoicePayment
                    {
                        Amount = paymentEntity.PaidAmount.Gross,
                        Paid = paymentEntity.InvoicePaidAmount.Net,
                        ReceivedDate = paymentEntity.ReceivedTime.DateTime,
                        AmountFormatted = displayFormatter.Currency(paymentEntity.PaidAmount.Gross, paymentEntity.PaidAmount.Currency),
                        PaidFormatted = displayFormatter.Currency(paymentEntity.InvoicePaidAmount.Net, invoice.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                        RateFormatted = displayFormatter.Currency(paymentEntity.Rate, invoice.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                        PaymentMethod = paymentMethodId.ToString(),
                        Link = link,
                        Id = txId,
                        Destination = paymentEntity.Destination
                    };
                })
                .Where(payment => payment != null)
                .ToList();
            }
            public string PaymentMethod { get; set; }
            public decimal Amount { get; set; }
            public string AmountFormatted { get; set; }
            public string RateFormatted { get; set; }
            public decimal Paid { get; set; }
            public string PaidFormatted { get; set; }
            public DateTime ReceivedDate { get; set; }
            public string Link { get; set; }
            public string Id { get; set; }
            public string Destination { get; set; }
            public string PaymentProof { get; set; }
        }
    }
}
