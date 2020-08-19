using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.SignalR;

namespace BTCPayServer.PaymentRequest
{
    public class PaymentRequestService
    {
        private readonly PaymentRequestRepository _PaymentRequestRepository;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;
        private readonly AppService _AppService;
        private readonly CurrencyNameTable _currencies;

        public PaymentRequestService(
            IHubContext<PaymentRequestHub> hubContext,
            PaymentRequestRepository paymentRequestRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            AppService appService,
            CurrencyNameTable currencies)
        {
            _PaymentRequestRepository = paymentRequestRepository;
            _BtcPayNetworkProvider = btcPayNetworkProvider;
            _AppService = appService;
            _currencies = currencies;
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

            if (currentStatus == Client.Models.PaymentRequestData.PaymentRequestStatus.Pending)
            {
                var rateRules = pr.StoreData.GetStoreBlob().GetRateRules(_BtcPayNetworkProvider);
                var invoices = await _PaymentRequestRepository.GetInvoicesForPaymentRequest(pr.Id);
                var contributions = _AppService.GetContributionsByPaymentMethodId(blob.Currency, invoices, true);
                if (contributions.TotalCurrency >= blob.Amount)
                {
                    currentStatus = Client.Models.PaymentRequestData.PaymentRequestStatus.Completed;
                }
            }

            if (currentStatus != pr.Status)
            {
                pr.Status = currentStatus;
                await _PaymentRequestRepository.UpdatePaymentRequestStatus(pr.Id, currentStatus);
            }
        }

        public async Task<ViewPaymentRequestViewModel> GetPaymentRequest(string id, string userId = null)
        {
            var pr = await _PaymentRequestRepository.FindPaymentRequest(id, null);
            if (pr == null)
            {
                return null;
            }

            var blob = pr.GetBlob();

            var invoices = await _PaymentRequestRepository.GetInvoicesForPaymentRequest(id);

            var paymentStats = _AppService.GetContributionsByPaymentMethodId(blob.Currency, invoices, true);
            var amountDue = blob.Amount - paymentStats.TotalCurrency;
            var pendingInvoice = invoices.OrderByDescending(entity => entity.InvoiceTime)
                .FirstOrDefault(entity => entity.Status == InvoiceStatus.New);

            return new ViewPaymentRequestViewModel(pr)
            {
                Archived = pr.Archived,
                AmountFormatted = _currencies.FormatCurrency(blob.Amount, blob.Currency),
                AmountCollected = paymentStats.TotalCurrency,
                AmountCollectedFormatted = _currencies.FormatCurrency(paymentStats.TotalCurrency, blob.Currency),
                AmountDue = amountDue,
                AmountDueFormatted = _currencies.FormatCurrency(amountDue, blob.Currency),
                CurrencyData = _currencies.GetCurrencyData(blob.Currency, true),
                LastUpdated = DateTime.Now,
                AnyPendingInvoice = pendingInvoice != null,
                PendingInvoiceHasPayments = pendingInvoice != null &&
                                            pendingInvoice.ExceptionStatus != InvoiceExceptionStatus.None,
                Invoices = invoices.Select(entity => new ViewPaymentRequestViewModel.PaymentRequestInvoice()
                {
                    Id = entity.Id,
                    Amount = entity.ProductInformation.Price,
                    AmountFormatted = _currencies.FormatCurrency(entity.ProductInformation.Price, blob.Currency),
                    Currency = entity.ProductInformation.Currency,
                    ExpiryDate = entity.ExpirationTime.DateTime,
                    Status = entity.GetInvoiceState().ToString(),
                    Payments = entity
                        .GetPayments()
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
                        return new ViewPaymentRequestViewModel.PaymentRequestInvoicePayment()
                        {
                            Amount = paymentData.GetValue(),
                            PaymentMethod = paymentMethodId.ToString(),
                            Link = link,
                            Id = txId
                        };
                    })
                        .Where(payment => payment != null)
                        .ToList()
                }).ToList()
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
