using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.Services
{
    public class DelayedTransactionBroadcaster
    {
        class Record
        {
            public string Id;
            public DateTimeOffset BroadcastTime;
            public Transaction Transaction;
            public BTCPayNetwork Network;
        }

        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly ApplicationDbContextFactory _dbContextFactory;

        public Logs Logs { get; }

        public DelayedTransactionBroadcaster(
            BTCPayNetworkProvider networkProvider,
            ExplorerClientProvider explorerClientProvider,
            Data.ApplicationDbContextFactory dbContextFactory,
            Logs logs)
        {
            ArgumentNullException.ThrowIfNull(explorerClientProvider);
            _networkProvider = networkProvider;
            _explorerClientProvider = explorerClientProvider;
            _dbContextFactory = dbContextFactory;
            this.Logs = logs;
        }

        public async Task Schedule(DateTimeOffset broadcastTime, Transaction transaction, BTCPayNetwork network)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(network);
            using var db = _dbContextFactory.CreateContext();
            var conn = db.Database.GetDbConnection();
            await conn.ExecuteAsync(
                """
                INSERT INTO "PlannedTransactions"("Id", "BroadcastAt", "Blob") VALUES(@Id, @BroadcastAt, @Blob)
                ON CONFLICT DO NOTHING
                """,
                new
                {
                    Id = $"{network.CryptoCode}-{transaction.GetHash()}",
                    BroadcastAt = broadcastTime,
                    Blob = transaction.ToBytes()
                });
        }

        public async Task<int> ProcessAll(CancellationToken cancellationToken = default)
        {
            if (disabled)
                return 0;
            List<Record> scheduled = new List<Record>();
            using (var db = _dbContextFactory.CreateContext())
            {
                scheduled = (await db.PlannedTransactions
                    .ToListAsync()).Select(ToRecord)
                    .Where(r => r != null)
                    // Client side filtering because entity framework is retarded.
                    .Where(r => r.BroadcastTime < DateTimeOffset.UtcNow).ToList();
            }

            List<Record> rescheduled = new List<Record>();
            List<Record> broadcasted = new List<Record>();

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

            using (var db = _dbContextFactory.CreateContext())
            {
                foreach (Record record in broadcasted)
                {
                    db.PlannedTransactions.Remove(new PlannedTransaction() { Id = record.Id });
                }
                return await db.SaveChangesAsync();
            }
        }

        private Record ToRecord(PlannedTransaction plannedTransaction)
        {
            var s = plannedTransaction.Id.Split('-');
            var network = _networkProvider.GetNetwork(s[0]) as BTCPayNetwork;
            if (network is null)
                return null;
            return new Record()
            {
                Id = plannedTransaction.Id,
                Network = network,
                Transaction = Transaction.Load(plannedTransaction.Blob, network.NBitcoinNetwork),
                BroadcastTime = plannedTransaction.BroadcastAt
            };
        }

        private bool disabled = false;
        public void Disable()
        {
            disabled = true;
        }

        public void Enable()
        {
            disabled = false;
        }
    }
}
