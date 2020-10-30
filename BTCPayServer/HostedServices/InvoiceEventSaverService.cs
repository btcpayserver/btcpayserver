using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.HostedServices
{
    public class InvoiceEventSaverService : EventHostedServiceBase
    {
        private readonly InvoiceRepository _invoiceRepository;

        public InvoiceEventSaverService(EventAggregator eventAggregator, InvoiceRepository invoiceRepository) : base(
            eventAggregator)
        {
            _invoiceRepository = invoiceRepository;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceDataChangedEvent>();
            Subscribe<InvoiceStopWatchedEvent>();
            Subscribe<BitPayInvoiceIPNEvent>();
            Subscribe<InvoiceEvent>();
            Subscribe<GreenFieldWebhookResultEvent>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            switch (evt)
            {
                case InvoiceDataChangedEvent e:
                    await SaveEvent(e.InvoiceId, e, InvoiceEventData.EventSeverity.Info);
                    break;
                case InvoiceStopWatchedEvent e:
                    await SaveEvent(e.InvoiceId, e, InvoiceEventData.EventSeverity.Info);
                    break;
                case InvoiceEvent e:
                    await SaveEvent(e.Invoice.Id, e, InvoiceEventData.EventSeverity.Info);
                    break;
                case BitPayInvoiceIPNEvent e:
                    await SaveEvent(e.InvoiceId, e,
                        string.IsNullOrEmpty(e.Error)
                            ? InvoiceEventData.EventSeverity.Success
                            : InvoiceEventData.EventSeverity.Error);
                    break;
                case GreenFieldWebhookResultEvent e when e.Hook.Scope is InvoiceWebhookScope webhookScope:
                    await SaveEvent(webhookScope.InvoiceId, e,
                        string.IsNullOrEmpty(e.Error)
                            ? InvoiceEventData.EventSeverity.Success
                            : InvoiceEventData.EventSeverity.Error);
                    break;
            }
        }

        private Task SaveEvent(string invoiceId, object evt, InvoiceEventData.EventSeverity severity)
        {
            return _invoiceRepository.AddInvoiceEvent(invoiceId, evt, severity);
        }
    }

    public class GreenFieldWebhookResultEvent
    {
        public  GreenFieldWebhookManager.QueuedGreenFieldWebHook Hook { get; set; }
        public string Error { get; set; }
        
        public override string ToString()
        {
            return string.IsNullOrEmpty(Error)
                ? $"Webhook {Hook.Subscription.EventType} sent to {Hook.Subscription.Url}"
                : $"Error while sending webhook {Hook.Subscription.EventType} to {Hook.Subscription.Url}: {Error}";
        }
    }
}
