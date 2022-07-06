using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Invoices
{
    public static class InvoiceExtensions
    {

        public static async Task<bool> ActivateInvoicePaymentMethod(this InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator, BTCPayNetworkProvider btcPayNetworkProvider, PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            StoreData store, InvoiceEntity invoice, PaymentMethodId paymentMethodId)
        {
            if (invoice.GetInvoiceState().Status != InvoiceStatusLegacy.New)
                return false;
            bool success = false;
            var eligibleMethodToActivate = invoice.GetPaymentMethod(paymentMethodId);
            if (!eligibleMethodToActivate.GetPaymentMethodDetails().Activated)
            {
                var payHandler = paymentMethodHandlerDictionary[paymentMethodId];
                var supportPayMethod = invoice.GetSupportedPaymentMethod()
                    .Single(method => method.PaymentId == paymentMethodId);
                var paymentMethod = invoice.GetPaymentMethod(paymentMethodId);
                var network = btcPayNetworkProvider.GetNetwork(paymentMethodId.CryptoCode);
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
                    await invoiceRepository.UpdateInvoicePaymentMethod(invoice.Id, eligibleMethodToActivate);
                    eventAggregator.Publish(new InvoicePaymentMethodActivated(paymentMethodId, invoice));
                    eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
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

                await invoiceRepository.AddInvoiceLogs(invoice.Id, logs);
            }
            return success;
        }
    }
}
