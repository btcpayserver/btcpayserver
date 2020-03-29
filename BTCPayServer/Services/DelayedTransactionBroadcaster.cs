using System;
using Microsoft.Extensions.Logging;
using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBXplorer;
using System.Threading.Channels;
using System.Threading;
using BTCPayServer.Logging;

namespace BTCPayServer.Services
{
    public class DelayedTransactionBroadcaster
    {
        class Record
        {
            public DateTimeOffset Recorded;
            public DateTimeOffset BroadcastTime;
            public Transaction Transaction;
            public BTCPayNetwork Network;
        }
        Channel<Record> _Records = Channel.CreateUnbounded<Record>();
        private readonly ExplorerClientProvider _explorerClientProvider;

        public DelayedTransactionBroadcaster(ExplorerClientProvider explorerClientProvider)
        {
            if (explorerClientProvider == null)
                throw new ArgumentNullException(nameof(explorerClientProvider));
            _explorerClientProvider = explorerClientProvider;
        }

        public Task Schedule(DateTimeOffset broadcastTime, Transaction transaction, BTCPayNetwork network)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            var now = DateTimeOffset.UtcNow;
            var record = new Record()
            {
                Recorded = now,
                BroadcastTime = broadcastTime,
                Transaction = transaction,
                Network = network
            };
            _Records.Writer.TryWrite(record);
            // TODO: persist
            return Task.CompletedTask;
        }

        public async Task ProcessAll(CancellationToken cancellationToken = default)
        {
            if (disabled)
                return;
            var now = DateTimeOffset.UtcNow;
            List<Record> rescheduled = new List<Record>();
            List<Record> scheduled = new List<Record>();
            List<Record> broadcasted = new List<Record>();
            while (_Records.Reader.TryRead(out var r))
            {
                (r.BroadcastTime > now ? rescheduled : scheduled).Add(r);
            }

            var broadcasts = scheduled.Select(async (record) =>
            {
                var explorer = _explorerClientProvider.GetExplorerClient(record.Network);
                if (explorer is null)
                    return false;
                try
                {
                    // We don't look the result, this is a best effort basis.
                    var result = await explorer.BroadcastAsync(record.Transaction, cancellationToken);
                    if (result.Success)
                    {
                        Logs.PayServer.LogInformation($"{record.Network.CryptoCode}: {record.Transaction.GetHash()} has been successfully broadcasted");
                    }
                    return false;
                }
                catch
                {
                    // If this goes here, maybe RPC is down or NBX is down, we should reschedule
                    return true;
                }
            }).ToArray();
            
            for (int i = 0; i < scheduled.Count; i++)
            {
                var needReschedule = await broadcasts[i];
                (needReschedule ? rescheduled : broadcasted).Add(scheduled[i]);
            }
            foreach (var record in rescheduled)
            {
                _Records.Writer.TryWrite(record);
            }
            // TODO: Remove everything in broadcasted from DB
        }

        private bool disabled = false;
        public void Disable()
        {
            disabled = true;
        }
    }
}
