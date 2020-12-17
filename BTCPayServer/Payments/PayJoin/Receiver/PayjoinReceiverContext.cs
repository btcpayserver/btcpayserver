using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitpayClient;
using NBXplorer;

namespace BTCPayServer.Payments.PayJoin
{
    public class PayjoinReceiverContext
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly ExplorerClient _explorerClient;
        private readonly PayJoinRepository _payJoinRepository;

        public PayjoinReceiverContext(InvoiceRepository invoiceRepository, ExplorerClient explorerClient, PayJoinRepository payJoinRepository)
        {
            _invoiceRepository = invoiceRepository;
            _explorerClient = explorerClient;
            _payJoinRepository = payJoinRepository;
        }
        public Invoice Invoice { get; set; }
        public NBitcoin.Transaction OriginalTransaction { get; set; }
        public InvoiceLogs Logs { get; } = new InvoiceLogs();
        public OutPoint[] LockedUTXOs { get; set; }
        public async Task DisposeAsync()
        {
            List<Task> disposing = new List<Task>();
            if (Invoice != null)
            {
                disposing.Add(_invoiceRepository.AddInvoiceLogs(Invoice.Id, Logs));
            }
            if (!doNotBroadcast && OriginalTransaction != null)
            {
                disposing.Add(_explorerClient.BroadcastAsync(OriginalTransaction));
            }
            if (!success && LockedUTXOs != null)
            {
                disposing.Add(_payJoinRepository.TryUnlock(LockedUTXOs));
            }
            try
            {
                await Task.WhenAll(disposing);
            }
            catch (Exception ex)
            {
                BTCPayServer.Logging.Logs.PayServer.LogWarning(ex, "Error while disposing the PayjoinReceiverContext");
            }
        }

        bool doNotBroadcast = false;
        public void DoNotBroadcast()
        {
            doNotBroadcast = true;
        }

        bool success = false;
        public void Success()
        {
            doNotBroadcast = true;
            success = true;
        }
    }
}
