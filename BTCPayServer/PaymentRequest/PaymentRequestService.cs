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
            var blob = pr.GetBlob();
            var currentStatus = pr.Status;
            if (blob.ExpiryDate.HasValue)
            {
                if (blob.ExpiryDate.Value <= DateTimeOffset.UtcNow)
                    currentStatus = Client.Models.PaymentRequestData.PaymentRequestStatus.Expired;
            }
            else if (currentStatus != Client.Models.PaymentRequestData.PaymentRequestStatus.Completed)
            {
                currentStatus = Client.Models.PaymentRequestData.PaymentRequestStatus.Pending;
            }

            if (currentStatus != Client.Models.PaymentRequestData.PaymentRequestStatus.Expired)
            {
                var invoices = await _paymentRequestRepository.GetInvoicesForPaymentRequest(pr.Id);
                var contributions = _invoiceRepository.GetContributionsByPaymentMethodId(blob.Currency, invoices, true);
                var allSettled = contributions.All(i => i.Value.Settled);
                var isPaid = contributions.TotalCurrency >= blob.Amount;

                if (isPaid)
                {
                    currentStatus = allSettled
                        ? Client.Models.PaymentRequestData.PaymentRequestStatus.Completed
                        : Client.Models.PaymentRequestData.PaymentRequestStatus.Processing;
                }
                else
                {
                    currentStatus = Client.Models.PaymentRequestData.PaymentRequestStatus.Pending;
                }
            }

            if (currentStatus != pr.Status)
            {
                pr.Status = currentStatus;
                await _paymentRequestRepository.UpdatePaymentRequestStatus(pr.Id, currentStatus);
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
            var paymentStats = _invoiceRepository.GetContributionsByPaymentMethodId(blob.Currency, invoices, true);
            var amountDue = blob.Amount - paymentStats.TotalCurrency;
            var pendingInvoice = invoices.OrderByDescending(entity => entity.InvoiceTime)
                .FirstOrDefault(entity => entity.Status == InvoiceStatus.New);
            
            return new ViewPaymentRequestViewModel(pr)
            {
                Archived = pr.Archived,
                AmountFormatted = _displayFormatter.Currency(blob.Amount, blob.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                AmountCollected = paymentStats.TotalCurrency,
                AmountCollectedFormatted = _displayFormatter.Currency(paymentStats.TotalCurrency, blob.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                AmountDue = amountDue,
                AmountDueFormatted = _displayFormatter.Currency(amountDue, blob.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                CurrencyData = _currencies.GetCurrencyData(blob.Currency, true),
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

                    if (state.Status == InvoiceStatus.Invalid ||
                        state.Status == InvoiceStatus.Expired && !payments.Any())
                        return null;

                    return new ViewPaymentRequestViewModel.PaymentRequestInvoice
                    {
                        Id = entity.Id,
                        Amount = entity.Price,
                        AmountFormatted = _displayFormatter.Currency(entity.Price, blob.Currency, DisplayFormatter.CurrencyFormat.Symbol),
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
