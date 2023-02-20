using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using NBitcoin;

namespace BTCPayServer.Services
{
    public class InvoiceActivator
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;
        private readonly WalletRepository _walletRepository;

        public InvoiceActivator(
            InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider btcPayNetworkProvider,
            PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            WalletRepository walletRepository)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
            _walletRepository = walletRepository;
        }
        public async Task<bool> ActivateInvoicePaymentMethod(PaymentMethodId paymentMethodId, InvoiceEntity invoice, StoreData store)
        {
            if (invoice.GetInvoiceState().Status != InvoiceStatusLegacy.New)
                return false;
            bool success = false;
            var eligibleMethodToActivate = invoice.GetPaymentMethod(paymentMethodId);
            if (!eligibleMethodToActivate.GetPaymentMethodDetails().Activated)
            {
                var payHandler = _paymentMethodHandlerDictionary[paymentMethodId];
                var supportPayMethod = invoice.GetSupportedPaymentMethod()
                    .Single(method => method.PaymentId == paymentMethodId);
                var paymentMethod = invoice.GetPaymentMethod(paymentMethodId);
                var network = _btcPayNetworkProvider.GetNetwork(paymentMethodId.CryptoCode);
                var prepare = payHandler.PreparePayment(supportPayMethod, store, network);
                InvoiceLogs logs = new InvoiceLogs();
                try
                {
                    var pmis = invoice.GetPaymentMethods().Select(method => method.GetId()).ToHashSet();
                    logs.Write($"{paymentMethodId}: Activating", InvoiceEventData.EventSeverity.Info);
                    var newDetails = await
                        payHandler.CreatePaymentMethodDetails(logs, supportPayMethod, paymentMethod, store, network,
                            prepare, pmis);
                    eligibleMethodToActivate.SetPaymentMethodDetails(newDetails);
                    await _invoiceRepository.UpdateInvoicePaymentMethod(invoice.Id, eligibleMethodToActivate);

                    if (newDetails is BitcoinLikeOnChainPaymentMethod bp)
                    {
                        var walletId = new WalletId(store.Id, paymentMethodId.CryptoCode);
                        if (bp.GetDepositAddress(((BTCPayNetwork)_btcPayNetworkProvider.GetNetwork(paymentMethodId.CryptoCode)).NBitcoinNetwork) is BitcoinAddress address)
                        {
                            await _walletRepository.EnsureWalletObjectLink(
                            new WalletObjectId(
                                walletId,
                                WalletObjectData.Types.Address,
                                address.ToString()),
                            new WalletObjectId(
                                walletId,
                                WalletObjectData.Types.Invoice,
                                invoice.Id));
                        }
                    }

                    _eventAggregator.Publish(new InvoicePaymentMethodActivated(paymentMethodId, invoice));
                    _eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
                    success = true;
                }
                catch (PaymentMethodUnavailableException ex)
                {
                    logs.Write($"{paymentMethodId}: Payment method unavailable ({ex.Message})", InvoiceEventData.EventSeverity.Error);
                }
                catch (Exception ex)
                {
                    logs.Write($"{paymentMethodId}: Unexpected exception ({ex})", InvoiceEventData.EventSeverity.Error);
                }

                await _invoiceRepository.AddInvoiceLogs(invoice.Id, logs);
            }
            return success;
        }
    }
}
