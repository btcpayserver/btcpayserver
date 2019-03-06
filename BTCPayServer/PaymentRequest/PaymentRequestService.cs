using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.Payments.Lightning;
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
                    currentStatus = PaymentRequestData.PaymentRequestStatus.Expired;
            }
            else if (pr.Status == PaymentRequestData.PaymentRequestStatus.Pending)
            {
                var rateRules = pr.StoreData.GetStoreBlob().GetRateRules(_BtcPayNetworkProvider);
                var invoices = await _PaymentRequestRepository.GetInvoicesForPaymentRequest(pr.Id);
                var contributions = _AppService.GetContributionsByPaymentMethodId(blob.Currency, invoices, true);
                if (contributions.TotalCurrency >= blob.Amount)
                {
                    currentStatus = PaymentRequestData.PaymentRequestStatus.Completed;
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
            var rateRules = pr.StoreData.GetStoreBlob().GetRateRules(_BtcPayNetworkProvider);

            var invoices = await _PaymentRequestRepository.GetInvoicesForPaymentRequest(id);

            var paymentStats = _AppService.GetContributionsByPaymentMethodId(blob.Currency, invoices, true);
            var amountDue = blob.Amount - paymentStats.TotalCurrency;

            return new ViewPaymentRequestViewModel(pr)
            {
                AmountFormatted = _currencies.FormatCurrency(blob.Amount, blob.Currency),
                AmountCollected = paymentStats.TotalCurrency,
                AmountCollectedFormatted = _currencies.FormatCurrency(paymentStats.TotalCurrency, blob.Currency),
                AmountDue = amountDue,
                AmountDueFormatted = _currencies.FormatCurrency(amountDue, blob.Currency),
                CurrencyData = _currencies.GetCurrencyData(blob.Currency, true),
                LastUpdated = DateTime.Now,
                AnyPendingInvoice = invoices.Any(entity => entity.Status == InvoiceStatus.New),
                Invoices = invoices.Select(entity => new ViewPaymentRequestViewModel.PaymentRequestInvoice()
                {
                    Id = entity.Id,
                    Amount = entity.ProductInformation.Price,
                    AmountFormatted = _currencies.FormatCurrency(entity.ProductInformation.Price, blob.Currency),
                    Currency = entity.ProductInformation.Currency,
                    ExpiryDate = entity.ExpirationTime.DateTime,
                    Status = entity.GetInvoiceState().ToString(),
                    Payments = entity.GetPayments().Select(paymentEntity =>
                    {
                        var paymentNetwork = _BtcPayNetworkProvider.GetNetwork(paymentEntity.GetCryptoCode());
                        var paymentData = paymentEntity.GetCryptoPaymentData();
                        string link = null;
                        string txId = null;
                        switch (paymentData)
                        {
                            case Payments.Bitcoin.BitcoinLikePaymentData onChainPaymentData:
                                txId = onChainPaymentData.Outpoint.Hash.ToString();
                                link = string.Format(CultureInfo.InvariantCulture, paymentNetwork.BlockExplorerLink,
                                    txId);
                                break;
                            case LightningLikePaymentData lightningLikePaymentData:
                                txId = lightningLikePaymentData.BOLT11;
                                break;
                        }

                        return new ViewPaymentRequestViewModel.PaymentRequestInvoicePayment()
                        {
                            Amount = paymentData.GetValue(),
                            PaymentMethod = paymentEntity.GetPaymentMethodId().ToString(),
                            Link = link,
                            Id = txId
                        };
                    }).ToList()
                }).ToList()
            };
        }
    }
}
