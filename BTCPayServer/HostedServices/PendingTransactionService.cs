#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.HostedServices;

public class PendingTransactionService(
    DelayedTransactionBroadcaster broadcaster,
    BTCPayNetworkProvider networkProvider,
    ApplicationDbContextFactory dbContextFactory,
    EventAggregator eventAggregator,
    ILogger<PendingTransactionService> logger,
    ExplorerClientProvider explorerClientProvider)
    : EventHostedServiceBase(eventAggregator, logger), IPeriodicTask
{
    protected override void SubscribeToEvents()
    {
        Subscribe<NewOnChainTransactionEvent>();
        base.SubscribeToEvents();
    }
    
    public Task Do(CancellationToken cancellationToken)
    {
        PushEvent(new CheckForExpiryEvent());
        return Task.CompletedTask;
    }

    public class CheckForExpiryEvent { } 

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is CheckForExpiryEvent)
        {
            await using var ctx = dbContextFactory.CreateContext();
            var pendingTransactions = await ctx.PendingTransactions
                .Where(p => p.Expiry <= DateTimeOffset.UtcNow && p.State == PendingTransactionState.Pending)
                .ToArrayAsync(cancellationToken: cancellationToken);
            foreach (var pendingTransaction in pendingTransactions)
            {
                pendingTransaction.State = PendingTransactionState.Expired;
            }

            await ctx.SaveChangesAsync(cancellationToken);
        }
        else if (evt is NewOnChainTransactionEvent newTransactionEvent)
        {
            await using var ctx = dbContextFactory.CreateContext();
            var txInputs = newTransactionEvent.NewTransactionEvent.TransactionData.Transaction.Inputs
                .Select(i => i.PrevOut.ToString()).ToArray();
            var txHash = newTransactionEvent.NewTransactionEvent.TransactionData.TransactionHash.ToString();
            var pendingTransactions = await ctx.PendingTransactions
                .Where(p => p.TransactionId == txHash || p.OutpointsUsed.Any(o => txInputs.Contains(o)))
                .ToArrayAsync(cancellationToken: cancellationToken);
            if (!pendingTransactions.Any())
            {
                return;
            }

            foreach (var pendingTransaction in pendingTransactions)
            {
                if (pendingTransaction.TransactionId == txHash)
                {
                    pendingTransaction.State = PendingTransactionState.Broadcast;
                    continue;
                }

                if (pendingTransaction.OutpointsUsed.Any(o => txInputs.Contains(o)))
                {
                    pendingTransaction.State = PendingTransactionState.Invalidated;
                }
            }

            await ctx.SaveChangesAsync(cancellationToken);
        }

        await base.ProcessEvent(evt, cancellationToken);
    }

    public async Task<PendingTransaction> CreatePendingTransaction(string storeId, string cryptoCode, PSBT psbt,
        DateTimeOffset? expiry = null, CancellationToken cancellationToken = default)
    {
        var network = networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network is null)
        {
            throw new NotSupportedException("CryptoCode not supported");
        }

        var txId = psbt.GetGlobalTransaction().GetHash();
        await using var ctx = dbContextFactory.CreateContext();
        var pendingTransaction = new PendingTransaction
        {
            CryptoCode = cryptoCode,
            TransactionId = txId.ToString(),
            State = PendingTransactionState.Pending,
            OutpointsUsed = psbt.Inputs.Select(i => i.PrevOut.ToString()).ToArray(),
            Expiry = expiry,
            StoreId = storeId,
        };
        pendingTransaction.SetBlob(new PendingTransactionBlob { PSBT = psbt.ToBase64() });
        ctx.PendingTransactions.Add(pendingTransaction);
        await ctx.SaveChangesAsync(cancellationToken);
        return pendingTransaction;
    }

    public async Task<PendingTransaction?> CollectSignature(string cryptoCode, PSBT psbt, bool broadcastIfComplete,
        CancellationToken cancellationToken)
    {
        var network = networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network is null)
        {
            return null;
        }

        var txId = psbt.GetGlobalTransaction().GetHash();
        await using var ctx = dbContextFactory.CreateContext();
        var pendingTransaction =
            await ctx.PendingTransactions.FindAsync(new object[] { cryptoCode, txId.ToString() }, cancellationToken);
        if (pendingTransaction is null)
        {
            return null;
        }

        if (pendingTransaction.State != PendingTransactionState.Pending)
        {
            return null;
        }

        var blob = pendingTransaction.GetBlob();
        if (blob?.PSBT is null)
        {
            return null;
        }
        var originalPsbtWorkingCopy = PSBT.Parse(blob.PSBT, network.NBitcoinNetwork);
        foreach (var collectedSignature in blob.CollectedSignatures)
        {
            var collectedPsbt = PSBT.Parse(collectedSignature.ReceivedPSBT, network.NBitcoinNetwork);
            originalPsbtWorkingCopy = originalPsbtWorkingCopy.Combine(collectedPsbt);
        }

        var originalPsbtWorkingCopyWithNewPsbt = originalPsbtWorkingCopy.Combine(psbt);
        //check if we have more signatures than before
        if (originalPsbtWorkingCopyWithNewPsbt.Inputs.All(i =>
                i.PartialSigs.Count >= originalPsbtWorkingCopy.Inputs[(int)i.Index].PartialSigs.Count))
        {
            blob.CollectedSignatures.Add(new CollectedSignature
            {
                ReceivedPSBT = psbt.ToBase64(), Timestamp = DateTimeOffset.UtcNow
            });
            pendingTransaction.SetBlob(blob);
        }

        if (originalPsbtWorkingCopyWithNewPsbt.TryFinalize(out _))
        {
            pendingTransaction.State = PendingTransactionState.Signed;
        }

        await ctx.SaveChangesAsync(cancellationToken);
        if (broadcastIfComplete && pendingTransaction.State == PendingTransactionState.Signed)
        {
            var explorerClient = explorerClientProvider.GetExplorerClient(network);
            var tx = originalPsbtWorkingCopyWithNewPsbt.ExtractTransaction();
            var result = await explorerClient.BroadcastAsync(tx, cancellationToken);
            if (result.Success)
            {
                pendingTransaction.State = PendingTransactionState.Broadcast;
                await ctx.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await broadcaster.Schedule(DateTimeOffset.Now, tx, network);
            }
        }

        return pendingTransaction;
    }

    public async Task<PendingTransaction?> GetPendingTransaction(string cryptoCode, string storeId, string txId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        return await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == cryptoCode && p.StoreId == storeId && p.TransactionId == txId);
    }

    public async Task<PendingTransaction[]> GetPendingTransactions(string cryptoCode, string storeId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        return await ctx.PendingTransactions.Where(p =>
                p.CryptoCode == cryptoCode && p.StoreId == storeId && (p.State == PendingTransactionState.Pending ||
                                                                       p.State == PendingTransactionState.Signed))
            .ToArrayAsync();
    }

    public async Task CancelPendingTransaction(string cryptoCode, string storeId, string transactionId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var pt = await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == cryptoCode && p.StoreId == storeId && p.TransactionId == transactionId &&
            (p.State == PendingTransactionState.Pending || p.State == PendingTransactionState.Signed));
        if (pt is null) return;
        pt.State = PendingTransactionState.Cancelled;
        await ctx.SaveChangesAsync();
    }

    public async Task Broadcasted(string cryptoCode, string storeId, string transactionId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var pt = await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == cryptoCode && p.StoreId == storeId && p.TransactionId == transactionId &&
            (p.State == PendingTransactionState.Pending || p.State == PendingTransactionState.Signed));
        if (pt is null) return;
        pt.State = PendingTransactionState.Broadcast;
        await ctx.SaveChangesAsync();
    }
}
