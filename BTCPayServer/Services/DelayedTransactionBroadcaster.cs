using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;

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
            public string PayjoinTransactionId;
            public string[] PayjoinOutpoints;
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

        public Task Schedule(DateTimeOffset broadcastTime, Transaction transaction, BTCPayNetwork network)
            => Schedule(broadcastTime, transaction, network, null, null);

        internal async Task Schedule(DateTimeOffset broadcastTime, Transaction transaction, BTCPayNetwork network,
            uint256 payjoinTransactionId, OutPoint[] payjoinOutpoints)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(network);
            using var db = _dbContextFactory.CreateContext();
            var conn = db.Database.GetDbConnection();
            await conn.ExecuteAsync(
                """
                INSERT INTO "PlannedTransactions"("Id", "BroadcastAt", "Blob", "PayjoinTransactionId", "PayjoinOutpoints")
                VALUES(@Id, @BroadcastAt, @Blob, @PayjoinTransactionId, @PayjoinOutpoints)
                ON CONFLICT ("Id") DO UPDATE SET
                    "PayjoinTransactionId" = EXCLUDED."PayjoinTransactionId",
                    "PayjoinOutpoints" = EXCLUDED."PayjoinOutpoints"
                WHERE "PlannedTransactions"."PayjoinTransactionId" IS NULL AND EXCLUDED."PayjoinTransactionId" IS NOT NULL
                """,
                new
                {
                    Id = $"{network.CryptoCode}-{transaction.GetHash()}",
                    BroadcastAt = broadcastTime,
                    Blob = transaction.ToBytes(),
                    PayjoinTransactionId = payjoinTransactionId?.ToString(),
                    PayjoinOutpoints = payjoinOutpoints?.Select(o => o.ToString()).ToArray()
                });
        }

        public async Task<int> ProcessAll(CancellationToken cancellationToken = default)
        {
            if (disabled)
                return 0;
            List<Record> scheduled = new List<Record>();
            using (var readContext = _dbContextFactory.CreateContext())
            {
                scheduled = (await readContext.PlannedTransactions
                    .ToListAsync(cancellationToken)).Select(ToRecord)
                    .Where(r => r != null)
                    // Client side filtering because entity framework is retarded.
                    .Where(r => r.BroadcastTime < DateTimeOffset.UtcNow).ToList();
            }

            List<Record> broadcasted = new List<Record>();

            var broadcasts = scheduled.Select(async (record) =>
            {
                var explorer = _explorerClientProvider.GetExplorerClient(record.Network);
                if (explorer is null)
                    return record.PayjoinTransactionId is not null;
                try
                {
                    if (record.PayjoinTransactionId is not null &&
                        (await explorer.GetTransactionAsync(uint256.Parse(record.PayjoinTransactionId), cancellationToken))?.Confirmations > 0)
                        return false;

                    var result = await explorer.BroadcastAsync(record.Transaction, cancellationToken);
                    if (result.Success)
                    {
                        Logs.PayServer.LogInformation($"{record.Network.CryptoCode}: {record.Transaction.GetHash()} has been successfully broadcasted");
                    }
                    return record.PayjoinTransactionId is not null && !result.Success &&
                           result.RPCCode != RPCErrorCode.RPC_TRANSACTION_ALREADY_IN_CHAIN;
                }
                catch
                {
                    // If this goes here, maybe RPC is down or NBX is down, we should reschedule
                    return true;
                }
            }).ToArray();

            for (int i = 0; i < scheduled.Count; i++)
            {
                if (!await broadcasts[i])
                    broadcasted.Add(scheduled[i]);
            }

            if (broadcasted.Count == 0)
                return 0;

            await using var db = _dbContextFactory.CreateContext();
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var removed = 0;
                foreach (var record in broadcasted.OrderBy(r => r.Id, StringComparer.Ordinal))
                {
                    var deleted = await db.PlannedTransactions
                        .Where(p => p.Id == record.Id && p.PayjoinTransactionId == record.PayjoinTransactionId)
                        .ExecuteDeleteAsync(cancellationToken);
                    if (deleted == 0)
                        continue;
                    removed += deleted;
                    var outpoints = record.PayjoinOutpoints;
                    if (outpoints is not null)
                        await db.PayjoinLocks.Where(l => outpoints.Contains(l.Id)).ExecuteDeleteAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                return removed;
            });
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
                BroadcastTime = plannedTransaction.BroadcastAt,
                PayjoinTransactionId = plannedTransaction.PayjoinTransactionId,
                PayjoinOutpoints = plannedTransaction.PayjoinOutpoints
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
