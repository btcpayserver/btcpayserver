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
        
        public static async Task ActivateInvoicePaymentMethod(this InvoiceRepository invoiceRepository, 
            EventAggregator eventAggregator, BTCPayNetworkProvider btcPayNetworkProvider, PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            StoreData store,InvoiceEntity invoice, PaymentMethodId paymentMethodId)
        {
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
                    logs.Write($"{paymentMethodId}: Activating", InvoiceEventData.EventSeverity.Info);
                    var newDetails = await
                        payHandler.CreatePaymentMethodDetails(logs, supportPayMethod, paymentMethod, store, network,
                            prepare);
                    eligibleMethodToActivate.SetPaymentMethodDetails(newDetails);
                    await invoiceRepository.UpdateInvoicePaymentMethod(invoice.Id, eligibleMethodToActivate);
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
                eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoice.Id));
            }
        }

    }
}
