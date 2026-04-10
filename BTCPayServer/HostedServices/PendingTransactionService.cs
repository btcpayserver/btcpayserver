#nullable enable
using System;
using System.Collections.Generic;
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

        var blob = new PendingTransactionBlob
        {
            PSBT = psbt.ToBase64(),
            RequestBaseUrl = requestBaseUrl.ToString()
        };
        ApplyProgress(blob, GetSignatureProgress(psbt));
        pendingTransaction.SetBlob(blob);

        ctx.PendingTransactions.Add(pendingTransaction);
        await ctx.SaveChangesAsync(cancellationToken);

        EventAggregator.Publish(new PendingTransactionEvent
        {
            Data = pendingTransaction,
            SignerUserId = null,
            Type = PendingTransactionEvent.Created
        });

        return pendingTransaction;
    }

    public async Task<PendingTransaction?> CollectSignature(PendingTransactionFullId id, PSBT psbt, CancellationToken cancellationToken, string? signerUserId = null)
    {
        const int maxAttempts = 3;
        var newPsbtBase64 = psbt.ToBase64();

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await using var ctx = dbContextFactory.CreateContext();
            try
            {
                var pendingTransaction = await TryCollectSignatureOnce(ctx, id, psbt, newPsbtBase64, cancellationToken);
                if (pendingTransaction is null)
                    return null;

                await ctx.SaveChangesAsync(cancellationToken);
                EventAggregator.Publish(new PendingTransactionEvent
                {
                    Data = pendingTransaction,
                    SignerUserId = signerUserId,
                    Type = PendingTransactionEvent.SignatureCollected
                });
                return pendingTransaction;
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts - 1)
            {
                // Another signer updated the pending transaction first. Re-read the row,
                // merge again on top of the latest effective PSBT, and retry.
            }
        }

        throw new DbUpdateConcurrencyException("Failed to collect pending transaction signatures due to concurrent updates.");
    }

    private async Task<PendingTransaction?> TryCollectSignatureOnce(
        ApplicationDbContext ctx,
        PendingTransactionFullId id,
        PSBT psbt,
        string newPsbtBase64,
        CancellationToken cancellationToken)
    {
        var pendingTransaction = await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == id.CryptoCode && p.StoreId == id.StoreId && p.Id == id.Id, cancellationToken);

        if (pendingTransaction?.State is not PendingTransactionState.Pending)
            return null;

        var blob = pendingTransaction.GetBlob();
        if (blob?.PSBT is null)
            return null;

        var network = networkProvider.GetNetwork<BTCPayNetwork>(pendingTransaction.CryptoCode)?.NBitcoinNetwork ?? psbt.Network;

        // Deduplicate exact duplicates before doing any merge work.
        if (blob.CollectedSignatures.Any(s => s.ReceivedPSBT == newPsbtBase64))
            return pendingTransaction;

        var beforeProgress = GetSignatureProgress(BuildEffectivePsbt(blob, network));
        var mergedPsbt = BuildEffectivePsbt(blob, network, psbt);
        var afterProgress = GetSignatureProgress(mergedPsbt);

        if (!HasMeaningfulDelta(beforeProgress, afterProgress))
            return pendingTransaction;

        blob.CollectedSignatures.Add(new CollectedSignature
        {
            ReceivedPSBT = newPsbtBase64,
            Timestamp = DateTimeOffset.UtcNow
        });
        ApplyProgress(blob, afterProgress);

        if (mergedPsbt.TryFinalize(out _))
        {
            if ((blob.SignaturesCollected ?? 0) < (blob.SignaturesNeeded ?? 0))
                blob.SignaturesCollected = blob.SignaturesNeeded;
            pendingTransaction.State = PendingTransactionState.Signed;
        }
        pendingTransaction.SetBlob(blob);
        return pendingTransaction;
    }


    public record PendingTransactionFullId(string CryptoCode, string StoreId, string Id);
    public async Task<PendingTransaction?> GetPendingTransaction(PendingTransactionFullId id)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var pendingTransaction = await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == id.CryptoCode && p.StoreId == id.StoreId && p.Id == id.Id);
        if (pendingTransaction is null)
            return null;
        if (TryRefreshStoredProgress(pendingTransaction))
            await ctx.SaveChangesAsync();
        return pendingTransaction;
    }

    public async Task<PendingTransaction[]> GetPendingTransactions(string cryptoCode, string storeId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var pendingTransactions = await ctx.PendingTransactions.Where(p =>
                p.CryptoCode == cryptoCode && p.StoreId == storeId && (p.State == PendingTransactionState.Pending ||
                                                                       p.State == PendingTransactionState.Signed))
            .ToArrayAsync();
        var changed = pendingTransactions.Aggregate(false, (current, pendingTransaction) => current | TryRefreshStoredProgress(pendingTransaction));

        if (changed)
            await ctx.SaveChangesAsync();
        return pendingTransactions;
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
            SignerUserId = null,
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
            SignerUserId = null,
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
        public string? SignerUserId { get; set; }
        public string Type { get; set; } = null!;
    }

    private bool TryRefreshStoredProgress(PendingTransaction pendingTransaction)
    {
        var blob = pendingTransaction.GetBlob();
        if (blob?.PSBT is null)
            return false;

        var network = networkProvider.GetNetwork<BTCPayNetwork>(pendingTransaction.CryptoCode)?.NBitcoinNetwork;
        if (network is null)
            return false;

        var progress = GetSignatureProgress(BuildEffectivePsbt(blob, network));
        if (blob.SignaturesNeeded == progress.SignaturesNeeded &&
            blob.SignaturesTotal == progress.SignaturesTotal &&
            blob.SignaturesCollected == progress.SignaturesCollected)
            return false;

        ApplyProgress(blob, progress);
        pendingTransaction.SetBlob(blob);
        return true;
    }

    private static PSBT BuildEffectivePsbt(PendingTransactionBlob blob, Network network, PSBT? additionalPsbt = null)
    {
        var effectivePsbt = PSBT.Parse(blob.PSBT, network);
        foreach (var collectedSignature in blob.CollectedSignatures)
        {
            effectivePsbt.Combine(PSBT.Parse(collectedSignature.ReceivedPSBT, network));
        }

        if (additionalPsbt is not null)
            effectivePsbt.Combine(additionalPsbt);

        return effectivePsbt;
    }

    private static PendingTransactionSignatureProgress GetSignatureProgress(PSBT psbt)
    {
        var inputs = new List<PendingTransactionInputProgress>(psbt.Inputs.Count);
        foreach (var input in psbt.Inputs)
        {
            var finalized = input.FinalScriptSig is not null || input.FinalScriptWitness is not null;
            var script = input.WitnessScript ?? input.RedeemScript;
            var multisigParams = script is null
                ? null
                : PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(script);

            if (multisigParams is null)
            {
                inputs.Add(new PendingTransactionInputProgress(false, 0, 0, input.PartialSigs.Count, finalized));
                continue;
            }

            var validExpectedPartialSigCount = input.PartialSigs.Keys.Count(multisigParams.PubKeys.Contains);
            var collected = finalized
                ? multisigParams.SignatureCount
                : Math.Min(validExpectedPartialSigCount, multisigParams.SignatureCount);
            inputs.Add(new PendingTransactionInputProgress(
                true,
                multisigParams.SignatureCount,
                multisigParams.PubKeys.Length,
                collected,
                finalized));
        }

        var multisigInputs = inputs.Where(i => i.IsMultisig).ToArray();
        if (multisigInputs.Length == 0)
            return new PendingTransactionSignatureProgress(0, 0, 0, inputs);

        // Broadcast readiness is limited by the least-signed multisig input, so the
        // user-facing transaction progress is the minimum collected count across inputs.
        return new PendingTransactionSignatureProgress(
            multisigInputs[0].SignaturesNeeded,
            multisigInputs[0].SignaturesTotal,
            multisigInputs.Min(i => i.SignaturesCollected),
            inputs);
    }

    private static bool HasMeaningfulDelta(PendingTransactionSignatureProgress before, PendingTransactionSignatureProgress after)
    {
        if (before.Inputs.Count != after.Inputs.Count)
            return true;

        for (var i = 0; i < before.Inputs.Count; i++)
        {
            var previous = before.Inputs[i];
            var current = after.Inputs[i];
            if (previous.IsMultisig != current.IsMultisig ||
                previous.SignaturesNeeded != current.SignaturesNeeded ||
                previous.SignaturesTotal != current.SignaturesTotal ||
                previous.SignaturesCollected != current.SignaturesCollected ||
                previous.IsFinalized != current.IsFinalized)
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplyProgress(PendingTransactionBlob blob, PendingTransactionSignatureProgress progress)
    {
        blob.SignaturesNeeded = progress.SignaturesNeeded;
        blob.SignaturesTotal = progress.SignaturesTotal;
        blob.SignaturesCollected = progress.SignaturesCollected;
    }

    private sealed record PendingTransactionSignatureProgress(
        int SignaturesNeeded,
        int SignaturesTotal,
        int SignaturesCollected,
        IReadOnlyList<PendingTransactionInputProgress> Inputs);

    private sealed record PendingTransactionInputProgress(
        bool IsMultisig,
        int SignaturesNeeded,
        int SignaturesTotal,
        int SignaturesCollected,
        bool IsFinalized);
}
