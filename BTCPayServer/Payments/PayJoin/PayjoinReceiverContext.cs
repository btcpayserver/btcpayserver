#nullable enable
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
        private readonly UTXOLocker _utxoLocker;
        private readonly BTCPayServer.Logging.Logs BTCPayLogs;
        public PayjoinReceiverContext(InvoiceRepository invoiceRepository, ExplorerClient explorerClient, UTXOLocker utxoLocker, BTCPayServer.Logging.Logs logs)
        {
            this.BTCPayLogs = logs;
            _invoiceRepository = invoiceRepository;
            _explorerClient = explorerClient;
            _utxoLocker = utxoLocker;
        }
        public InvoiceEntity? Invoice { get; set; }
        public NBitcoin.Transaction? OriginalTransaction { get; set; }
        public InvoiceLogs Logs { get; } = new InvoiceLogs();
        public OutPoint[]? LockedUTXOs { get; set; }
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
                disposing.Add(_utxoLocker.TryUnlock(LockedUTXOs));
            }
            try
            {
                await Task.WhenAll(disposing);
            }
            catch (Exception ex)
            {
                BTCPayLogs.PayServer.LogWarning(ex, "Error while disposing the PayjoinReceiverContext");
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
