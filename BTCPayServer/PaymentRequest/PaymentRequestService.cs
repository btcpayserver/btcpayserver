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
using Microsoft.AspNetCore.SignalR;
using PaymentRequestData = BTCPayServer.Data.PaymentRequestData;

namespace BTCPayServer.PaymentRequest
{
    public class PaymentRequestService
    {
        private readonly PaymentRequestRepository _PaymentRequestRepository;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly CurrencyNameTable _currencies;
        private readonly DisplayFormatter _displayFormatter;

        public PaymentRequestService(
            PaymentRequestRepository paymentRequestRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            InvoiceRepository invoiceRepository,
            AppService appService,
            DisplayFormatter displayFormatter,
            CurrencyNameTable currencies)
        {
            _PaymentRequestRepository = paymentRequestRepository;
            _BtcPayNetworkProvider = btcPayNetworkProvider;
            _invoiceRepository = invoiceRepository;
            _currencies = currencies;
            _displayFormatter = displayFormatter;
        }

        public async Task UpdatePaymentRequestStateIfNeeded(string id)
        {
            var pr = await _PaymentRequestRepository.FindPaymentRequest(id, null);
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
                var invoices = await _PaymentRequestRepository.GetInvoicesForPaymentRequest(pr.Id);
                var contributions = _invoiceRepository.GetContributionsByPaymentMethodId(blob.Currency, invoices, true);

                currentStatus = contributions.TotalCurrency >= blob.Amount
                    ? Client.Models.PaymentRequestData.PaymentRequestStatus.Completed
                    : Client.Models.PaymentRequestData.PaymentRequestStatus.Pending;
            }

            if (currentStatus != pr.Status)
            {
                pr.Status = currentStatus;
                await _PaymentRequestRepository.UpdatePaymentRequestStatus(pr.Id, currentStatus);
            }
        }

        public async Task<ViewPaymentRequestViewModel> GetPaymentRequest(string id, string userId = null)
        {
            var pr = await _PaymentRequestRepository.FindPaymentRequest(id, userId);
            if (pr == null)
            {
                return null;
            }

            var blob = pr.GetBlob();

            var invoices = await _PaymentRequestRepository.GetInvoicesForPaymentRequest(id);

            var paymentStats = _invoiceRepository.GetContributionsByPaymentMethodId(blob.Currency, invoices, true);
            var amountDue = blob.Amount - paymentStats.TotalCurrency;
            var pendingInvoice = invoices.OrderByDescending(entity => entity.InvoiceTime)
                .FirstOrDefault(entity => entity.Status == InvoiceStatusLegacy.New);

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
                    var payments = entity
                        .GetPayments(true)
                        .Select(paymentEntity =>
                        {
                            var paymentData = paymentEntity.GetCryptoPaymentData();
                            var paymentMethodId = paymentEntity.GetPaymentMethodId();
                            if (paymentData is null || paymentMethodId is null)
                            {
                                return null;
                            }

                            string txId = paymentData.GetPaymentId();
                            string link = GetTransactionLink(paymentMethodId, txId);
                            var paymentMethod = entity.GetPaymentMethod(paymentMethodId);
                            var amount = paymentData.GetValue();
                            var rate = paymentMethod.Rate;
                            var paid = (amount - paymentEntity.NetworkFee) * rate;

                            return new ViewPaymentRequestViewModel.PaymentRequestInvoicePayment
                            {
                                Amount = amount,
                                Paid = paid,
                                ReceivedDate = paymentEntity.ReceivedTime.DateTime,
                                PaidFormatted = _displayFormatter.Currency(paid, blob.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                                RateFormatted = _displayFormatter.Currency(rate, blob.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                                PaymentMethod = paymentMethodId.ToPrettyString(),
                                Link = link,
                                Id = txId,
                                Destination = paymentData.GetDestination()
                            };
                        })
                        .Where(payment => payment != null)
                        .ToList();

                    if (state.Status == InvoiceStatusLegacy.Invalid ||
                        state.Status == InvoiceStatusLegacy.Expired && !payments.Any())
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

        private string GetTransactionLink(PaymentMethodId paymentMethodId, string txId)
        {
            var network = _BtcPayNetworkProvider.GetNetwork(paymentMethodId.CryptoCode);
            if (network == null)
                return null;
            return paymentMethodId.PaymentType.GetTransactionLink(network, txId);
        }
    }
}
