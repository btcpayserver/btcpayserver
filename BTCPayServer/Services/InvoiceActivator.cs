using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using NBitcoin;
using static BTCPayServer.Client.Models.InvoicePaymentMethodDataModel;

namespace BTCPayServer.Services
{
    public class InvoiceActivator
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly StoreRepository _storeRepository;
        private readonly WalletRepository _walletRepository;

        public InvoiceActivator(
            InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider btcPayNetworkProvider,
            PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            StoreRepository storeRepository,
            WalletRepository walletRepository)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _handlers = paymentMethodHandlerDictionary;
            _storeRepository = storeRepository;
            _walletRepository = walletRepository;
        }
        public async Task<bool> ActivateInvoicePaymentMethod(string invoiceId, PaymentMethodId paymentMethodId, bool forceNew = false)
        {
            var invoice = await _invoiceRepository.GetInvoice(invoiceId);
            if (invoice?.GetInvoiceState().Status is not InvoiceStatus.New)
                return false;
            var store = await _storeRepository.FindStore(invoice.StoreId);
            if (store is null)
                return false;
            bool success = false;
            var paymentPrompt = invoice.GetPaymentPrompt(paymentMethodId);
            var wasAlreadyActivated = paymentPrompt.Activated;
            if (!paymentPrompt.Activated || forceNew)
            {
                if (!_handlers.TryGetValue(paymentMethodId, out var handler))
                    return false;
                InvoiceLogs logs = new InvoiceLogs();
                var paymentContext = new PaymentMethodContext(store, store.GetStoreBlob(), store.GetPaymentMethodConfig(paymentMethodId), handler, invoice, logs);
                if (!paymentPrompt.Activated)
                    paymentContext.Logs.Write("Activating", InvoiceEventData.EventSeverity.Info);
                try
                {
                    await paymentContext.BeforeFetchingRates();
                    await paymentContext.CreatePaymentPrompt();
                    if (paymentContext.Status == PaymentMethodContext.ContextStatus.Created)
                    {
                        await _invoiceRepository.NewPaymentPrompt(invoice.Id, paymentContext);
                        await paymentContext.ActivatingPaymentPrompt();

                        _eventAggregator.Publish(new InvoicePaymentMethodActivated(paymentMethodId, invoice));
                        _eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
                        if (wasAlreadyActivated)
                            _eventAggregator.Publish(new InvoiceNewPaymentDetailsEvent(invoice.Id, handler.ParsePaymentPromptDetails(paymentPrompt.Details), paymentMethodId));
                        success = true;
                    }
                }
                catch (PaymentMethodUnavailableException ex)
                {
                    logs.Write($"{paymentMethodId}: Payment method unavailable ({ex.Message})", InvoiceEventData.EventSeverity.Error);
                }
                catch (Exception ex)
                {
                    logs.Write($"{paymentMethodId}: Unexpected exception ({ex})", InvoiceEventData.EventSeverity.Error);
                }

                _ = _invoiceRepository.AddInvoiceLogs(invoice.Id, logs);
            }
            return success;
        }
    }
}
