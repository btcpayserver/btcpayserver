using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;
using NBXplorer;

namespace BTCPayServer.HostedServices
{
    public class InvoiceEventSaverService : EventHostedServiceBase
    {
        private readonly InvoiceRepository _invoiceRepository;
        public InvoiceEventSaverService(EventAggregator eventAggregator, InvoiceRepository invoiceRepository, Logs logs) : base(
            eventAggregator, logs)
        {
            _invoiceRepository = invoiceRepository;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
            Subscribe<InvoiceDataChangedEvent>();
            Subscribe<InvoiceIPNEvent>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            var e = (IHasInvoiceId)evt;
            var severity = InvoiceEventData.EventSeverity.Info;
            if (evt is InvoiceIPNEvent ipn)
            {
                severity = string.IsNullOrEmpty(ipn.Error) ? InvoiceEventData.EventSeverity.Success
                                                           : InvoiceEventData.EventSeverity.Error;
            }
            await _invoiceRepository.AddInvoiceEvent(e.InvoiceId, e, severity);
        }
    }
}
