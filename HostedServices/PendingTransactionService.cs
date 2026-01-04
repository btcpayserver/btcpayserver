#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Data;
using BTCPayServer.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.HostedServices;

public class PendingTransactionService(
    BTCPayNetworkProvider networkProvider,
    ApplicationDbContextFactory dbContextFactory,
    EventAggregator eventAggregator,
    ILogger<PendingTransactionService> logger)
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
            var cryptoCode = newTransactionEvent.NewTransactionEvent.CryptoCode;
            var txInputs = newTransactionEvent.NewTransactionEvent.TransactionData.Transaction.Inputs
                .Select(i => i.PrevOut.ToString()).ToArray();
            var txHash = newTransactionEvent.NewTransactionEvent.TransactionData.TransactionHash.ToString();
            var pendingTransactions = await ctx.PendingTransactions
                .Where(p => p.CryptoCode == cryptoCode && (p.TransactionId == txHash || p.OutpointsUsed.Any(o => txInputs.Contains(o))))
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
        RequestBaseUrl requestBaseUrl,
        DateTimeOffset? expiry = null, CancellationToken cancellationToken = default)
    {
        var network = networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network is null)
            throw new NotSupportedException("CryptoCode not supported");

        var txId = psbt.GetGlobalTransaction().GetHash();

        int signaturesNeeded = 0;
        int signaturesTotal = 0;

        foreach (var input in psbt.Inputs)
        {
            var script = input.WitnessScript ?? input.RedeemScript;
            if (script is null)
                continue;

            var multisigParams = PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(script);
            if (multisigParams != null)
            {
                signaturesNeeded = multisigParams.SignatureCount;
                signaturesTotal = multisigParams.PubKeys.Length;
                break; // assume consistent multisig scheme across all inputs
            }
        }

        await using var ctx = dbContextFactory.CreateContext();
        var pendingTransaction = new PendingTransaction
        {
            Id = Guid.NewGuid().ToString(),
            CryptoCode = cryptoCode,
            TransactionId = txId.ToString(),
            State = PendingTransactionState.Pending,
            OutpointsUsed = psbt.Inputs.Select(i => i.PrevOut.ToString()).ToArray(),
            Expiry = expiry,
            StoreId = storeId,
        };

        pendingTransaction.SetBlob(new PendingTransactionBlob
        {
            PSBT = psbt.ToBase64(),
            SignaturesCollected = 0,
            SignaturesNeeded = signaturesNeeded,
            SignaturesTotal = signaturesTotal,
            RequestBaseUrl = requestBaseUrl.ToString()
        });

        ctx.PendingTransactions.Add(pendingTransaction);
        await ctx.SaveChangesAsync(cancellationToken);

        EventAggregator.Publish(new PendingTransactionEvent
        {
            Data = pendingTransaction,
            Type = PendingTransactionEvent.Created
        });

        return pendingTransaction;
    }

    public async Task<PendingTransaction?> CollectSignature(PendingTransactionFullId id, PSBT psbt, CancellationToken cancellationToken)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var pendingTransaction = await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == id.CryptoCode && p.StoreId == id.StoreId && p.Id == id.Id, cancellationToken);

        if (pendingTransaction?.State is not PendingTransactionState.Pending)
        {
            return null;
        }

        var blob = pendingTransaction.GetBlob();
        if (blob?.PSBT is null)
        {
            return null;
        }

        var dbPsbt = PSBT.Parse(blob.PSBT, psbt.Network);

        // Deduplicate: Check if this exact PSBT (Base64) was already collected
        var newPsbtBase64 = psbt.ToBase64();
        if (blob.CollectedSignatures.Any(s => s.ReceivedPSBT == newPsbtBase64))
        {
            return pendingTransaction; // Avoid duplicate signature collection
        }

        foreach (var collectedSignature in blob.CollectedSignatures)
        {
            var collectedPsbt = PSBT.Parse(collectedSignature.ReceivedPSBT, psbt.Network);
            dbPsbt.Combine(collectedPsbt); // combine changes the object
        }

        var newWorkingCopyPsbt = dbPsbt.Clone(); // Clone before modifying
        newWorkingCopyPsbt.Combine(psbt);

        // Check if new signatures were actually added
        var oldPubKeys = dbPsbt.Inputs
            .SelectMany(input => input.PartialSigs.Keys)
            .ToHashSet();

        var newPubKeys = newWorkingCopyPsbt.Inputs
            .SelectMany(input => input.PartialSigs.Keys)
            .ToHashSet();

        newPubKeys.ExceptWith(oldPubKeys);

        var newSignatures = newPubKeys.Count;
        if (newSignatures > 0)
        {
            // TODO: For now we're going with estimation of how many signatures were collected until we find better way
            // so for example if we have 4 new signatures and only 2 inputs - number of collected signatures will be 2
            blob.SignaturesCollected += newSignatures / newWorkingCopyPsbt.Inputs.Count;
            blob.CollectedSignatures.Add(new CollectedSignature
            {
                ReceivedPSBT = newPsbtBase64,
                Timestamp = DateTimeOffset.UtcNow
            });
            pendingTransaction.SetBlob(blob);
        }

        if (newWorkingCopyPsbt.TryFinalize(out _))
        {
            // TODO: Better logic here
            if (blob.SignaturesCollected < blob.SignaturesNeeded)
                blob.SignaturesCollected = blob.SignaturesNeeded;

            pendingTransaction.State = PendingTransactionState.Signed;
        }

        await ctx.SaveChangesAsync(cancellationToken);
        EventAggregator.Publish(new PendingTransactionEvent
        {
            Data = pendingTransaction,
            Type = PendingTransactionEvent.SignatureCollected
        });
        return pendingTransaction;
    }


    public record PendingTransactionFullId(string CryptoCode, string StoreId, string Id);
    public async Task<PendingTransaction?> GetPendingTransaction(PendingTransactionFullId id)
    {
        await using var ctx = dbContextFactory.CreateContext();
        return await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == id.CryptoCode && p.StoreId == id.StoreId && p.Id == id.Id);
    }

    public async Task<PendingTransaction[]> GetPendingTransactions(string cryptoCode, string storeId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        return await ctx.PendingTransactions.Where(p =>
                p.CryptoCode == cryptoCode && p.StoreId == storeId && (p.State == PendingTransactionState.Pending ||
                                                                       p.State == PendingTransactionState.Signed))
            .ToArrayAsync();
    }

    public async Task CancelPendingTransaction(PendingTransactionFullId id)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var pt = await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == id.CryptoCode && p.StoreId == id.StoreId && p.Id == id.Id &&
            (p.State == PendingTransactionState.Pending || p.State == PendingTransactionState.Signed));
        if (pt is null) return;
        pt.State = PendingTransactionState.Cancelled;
        await ctx.SaveChangesAsync();
        EventAggregator.Publish(new PendingTransactionEvent
        {
            Data = pt,
            Type = PendingTransactionEvent.Cancelled
        });
    }

    public async Task Broadcasted(PendingTransactionFullId id)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var pt = await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == id.CryptoCode && p.StoreId == id.StoreId && p.Id == id.Id &&
            (p.State == PendingTransactionState.Pending || p.State == PendingTransactionState.Signed));
        if (pt is null) return;
        pt.State = PendingTransactionState.Broadcast;
        await ctx.SaveChangesAsync();
        EventAggregator.Publish(new PendingTransactionEvent
        {
            Data = pt,
            Type = PendingTransactionEvent.Broadcast
        });
    }

    public record PendingTransactionEvent
    {
        public const string Created = nameof(Created);
        public const string SignatureCollected = nameof(SignatureCollected);
        public const string Broadcast = nameof(Broadcast);
        public const string Cancelled = nameof(Cancelled);

        public PendingTransaction Data { get; set; } = null!;
        public string Type { get; set; } = null!;
    }

}
