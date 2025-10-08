using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using static System.Runtime.InteropServices.JavaScript.JSType;
using PaymentRequestData = BTCPayServer.Data.PaymentRequestData;

namespace BTCPayServer.PaymentRequest
{
    public class PaymentRequestService
    {
        private readonly PaymentRequestRepository _paymentRequestRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly CurrencyNameTable _currencies;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly TransactionLinkProviders _transactionLinkProviders;
        private readonly DisplayFormatter _displayFormatter;

        public PaymentRequestService(
            PaymentRequestRepository paymentRequestRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            InvoiceRepository invoiceRepository,
            DisplayFormatter displayFormatter,
            CurrencyNameTable currencies,
            PaymentMethodHandlerDictionary handlers,
            TransactionLinkProviders transactionLinkProviders)
        {
            _paymentRequestRepository = paymentRequestRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _invoiceRepository = invoiceRepository;
            _currencies = currencies;
            _handlers = handlers;
            _transactionLinkProviders = transactionLinkProviders;
            _displayFormatter = displayFormatter;
        }

        public async Task UpdatePaymentRequestStateIfNeeded(string id)
        {
            var pr = await _paymentRequestRepository.FindPaymentRequest(id, null);
            await UpdatePaymentRequestStateIfNeeded(pr);
        }

        public async Task UpdatePaymentRequestStateIfNeeded(PaymentRequestData pr)
        {
            var newStatus = pr.Status;
            newStatus = pr switch
            {
                { Expirable: true, Expiry: { } e }
                    when e <= DateTimeOffset.UtcNow => PaymentRequestStatus.Expired,
                { Status: PaymentRequestStatus.Expired, Expiry: null } => PaymentRequestStatus.Pending,
                _ => pr.Status
            };

            if (newStatus is not (PaymentRequestStatus.Expired or PaymentRequestStatus.Completed))
            {
                var invoices = await _paymentRequestRepository.GetInvoicesForPaymentRequest(pr.Id);
                var contributions = _invoiceRepository.GetContributionsByPaymentMethodId(pr.Currency, invoices, true);

                newStatus =
                    (PaidEnough: contributions.Total >= pr.Amount,
                    SettledEnough: contributions.TotalSettled >= pr.Amount) switch
                    {
                        { SettledEnough: true } => Client.Models.PaymentRequestStatus.Completed,
                        { PaidEnough: true } => Client.Models.PaymentRequestStatus.Processing,
                        _ => Client.Models.PaymentRequestStatus.Pending
                    };
            }

            if (newStatus != pr.Status)
            {
                pr.Status = newStatus;
                await _paymentRequestRepository.UpdatePaymentRequestStatus(pr.Id, newStatus);
            }
        }

        public async Task<ViewPaymentRequestViewModel> GetPaymentRequest(string id, string userId = null)
        {
            var pr = await _paymentRequestRepository.FindPaymentRequest(id, userId);
            if (pr == null)
            {
                return null;
            }

            var blob = pr.GetBlob();
            var invoices = await _paymentRequestRepository.GetInvoicesForPaymentRequest(id);
            var paymentStats = _invoiceRepository.GetContributionsByPaymentMethodId(pr.Currency, invoices, true);
            var amountDue = pr.Amount - paymentStats.Total;
            var pendingInvoice = invoices.OrderByDescending(entity => entity.InvoiceTime)
                .FirstOrDefault(entity => entity.Status == InvoiceStatus.New);

            return new ViewPaymentRequestViewModel(pr)
            {
                Archived = pr.Archived,
                AmountFormatted = _displayFormatter.Currency(pr.Amount, pr.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                AmountCollected = paymentStats.Total,
                AmountCollectedFormatted = _displayFormatter.Currency(paymentStats.Total, pr.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                AmountDue = amountDue,
                AmountDueFormatted = _displayFormatter.Currency(amountDue, pr.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                CurrencyData = _currencies.GetCurrencyData(pr.Currency, true),
                LastUpdated = DateTime.UtcNow,
                FormId = blob.FormId,
                FormSubmitted = blob.FormResponse is not null,
                AnyPendingInvoice = pendingInvoice != null,
                PendingInvoiceHasPayments = pendingInvoice != null &&
                                            pendingInvoice.ExceptionStatus != InvoiceExceptionStatus.None,
                Invoices = new ViewPaymentRequestViewModel.InvoiceList(invoices.Select(entity =>
                {
                    var state = entity.GetInvoiceState();
                    var payments = ViewPaymentRequestViewModel.PaymentRequestInvoicePayment.GetViewModels(entity, _displayFormatter, _transactionLinkProviders, _handlers);

                    if (state.Status is InvoiceStatus.Invalid or InvoiceStatus.Expired && payments.Count is 0)
                        return null;

                    return new ViewPaymentRequestViewModel.PaymentRequestInvoice
                    {
                        Id = entity.Id,
                        Amount = entity.Price,
                        AmountFormatted = _displayFormatter.Currency(entity.Price, pr.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                        Currency = entity.Currency,
                        ExpiryDate = entity.ExpirationTime.DateTime,
                        State = state,
                        StateFormatted = state.ToString(),
                        Payments = payments
                    };
                })
                .Where(invoice => invoice != null))
            };
        }
    }
}
