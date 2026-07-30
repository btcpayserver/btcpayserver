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
            var transaction = newTransactionEvent.NewTransactionEvent.TransactionData.Transaction;
            var txInputs = transaction.Inputs
                .Select(i => i.PrevOut.ToString()).ToArray();
            var txHash = newTransactionEvent.NewTransactionEvent.TransactionData.TransactionHash.ToString();
            var noSignatureTxHash = GetNoSignatureHash(transaction).ToString();
            var pendingTransactions = await ctx.PendingTransactions
                .Where(p => p.CryptoCode == cryptoCode && (p.NoSignatureTransactionId == noSignatureTxHash || p.OutpointsUsed.Any(o => txInputs.Contains(o))))
                .ToArrayAsync(cancellationToken: cancellationToken);
            if (!pendingTransactions.Any())
            {
                return;
            }

            foreach (var pendingTransaction in pendingTransactions)
            {
                if (pendingTransaction.State == PendingTransactionState.Broadcast)
                    continue;

                if (pendingTransaction.NoSignatureTransactionId == noSignatureTxHash)
                {
                    pendingTransaction.State = PendingTransactionState.Broadcast;
                    pendingTransaction.TransactionId = txHash;
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

        var noSignatureTransactionId = psbt.GetGlobalTransaction().GetHash();
        // If the transaction can't be malleated by a third party, it is safe to show the transaction ID.
        var malleabilitySafe = psbt.Inputs.All(i => i.GetCoin()?.IsMalleable is false);
        await using var ctx = dbContextFactory.CreateContext();
        var pendingTransaction = new PendingTransaction
        {
            Id = Guid.NewGuid().ToString(),
            CryptoCode = cryptoCode,
            NoSignatureTransactionId = noSignatureTransactionId.ToString(),
            TransactionId = malleabilitySafe ? noSignatureTransactionId.ToString() : null,
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
        var attempt = 0;
        var newPsbtBase64 = psbt.ToBase64();

    retry:
        await using (var ctx = dbContextFactory.CreateContext())
        {
            try
            {
                var result = await TryCollectSignatureOnce(ctx, id, psbt, newPsbtBase64, cancellationToken);
                if (result.PendingTransaction is null)
                    return null;

                if (!result.Changed)
                    return result.PendingTransaction;

                await ctx.SaveChangesAsync(cancellationToken);
                EventAggregator.Publish(new PendingTransactionEvent
                {
                    Data = result.PendingTransaction,
                    SignerUserId = signerUserId,
                    Type = PendingTransactionEvent.SignatureCollected
                });
                return result.PendingTransaction;
            }
            catch (DbUpdateConcurrencyException) when (++attempt < maxAttempts)
            {
                // Another signer updated the row first; re-read and merge onto the latest PSBT.
                goto retry;
            }
        }
    }

    private async Task<(PendingTransaction? PendingTransaction, bool Changed)> TryCollectSignatureOnce(
        ApplicationDbContext ctx,
        PendingTransactionFullId id,
        PSBT psbt,
        string newPsbtBase64,
        CancellationToken cancellationToken)
    {
        var pendingTransaction = await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == id.CryptoCode && p.StoreId == id.StoreId && p.Id == id.Id, cancellationToken);

        if (pendingTransaction?.State is not PendingTransactionState.Pending)
            return (null, false);

        var blob = pendingTransaction.GetBlob();
        if (blob?.PSBT is null)
            return (null, false);

        var network = networkProvider.GetNetwork<BTCPayNetwork>(pendingTransaction.CryptoCode)?.NBitcoinNetwork ?? psbt.Network;

        if (blob.CollectedSignatures.Any(s => s.ReceivedPSBT == newPsbtBase64))
        {
            logger.LogInformation(
                "Skipping finalization retry for pending transaction {PendingTransactionId}: this PSBT was already collected",
                pendingTransaction.Id);
            return (pendingTransaction, false);
        }

        var mergedPsbt = BuildEffectivePsbt(blob, network);
        var beforeProgress = GetSignatureProgress(mergedPsbt);
        mergedPsbt.Combine(psbt);
        var afterProgress = GetSignatureProgress(mergedPsbt);
        var meaningfulDelta = HasMeaningfulDelta(beforeProgress, afterProgress);

        var finalized = mergedPsbt.TryFinalize(out var finalizationErrors);
        var retained = meaningfulDelta || finalized;
        if (retained)
        {
            blob.CollectedSignatures.Add(new CollectedSignature
            {
                ReceivedPSBT = newPsbtBase64,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        ApplyProgress(blob, afterProgress);

        logger.LogInformation(
            "Retried finalization for pending transaction {PendingTransactionId}. Retained PSBT count: {CollectedPSBTCount}; " +
            "signature progress: {SignaturesCollected}/{SignaturesNeeded}; PSBT retained: {PSBTRetained}",
            pendingTransaction.Id,
            blob.CollectedSignatures.Count,
            blob.SignaturesCollected,
            blob.SignaturesNeeded,
            retained);

        if (finalized)
        {
            if ((blob.SignaturesCollected ?? 0) < (blob.SignaturesNeeded ?? 0))
                blob.SignaturesCollected = blob.SignaturesNeeded;
            pendingTransaction.State = PendingTransactionState.Signed;
            logger.LogInformation(
                "Finalized pending transaction {PendingTransactionId} after collecting PSBT {CollectedPSBTCount}",
                pendingTransaction.Id,
                blob.CollectedSignatures.Count);
        }
        else
        {
            var finalizationErrorDetails = FormatFinalizationErrors(finalizationErrors);
            var signaturesNeeded = blob.SignaturesNeeded ?? 0;
            var logLevel = signaturesNeeded > 0 &&
                           (blob.SignaturesCollected ?? 0) >= signaturesNeeded
                ? LogLevel.Warning
                : LogLevel.Debug;
            logger.Log(
                logLevel,
                "Finalization attempt failed for pending transaction {PendingTransactionId}. Signature progress: " +
                "{SignaturesCollected}/{SignaturesNeeded}. Errors: {FinalizationErrors}",
                pendingTransaction.Id,
                blob.SignaturesCollected,
                blob.SignaturesNeeded,
                finalizationErrorDetails);
        }
        pendingTransaction.SetBlob(blob);
        return (pendingTransaction, retained);
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

    public async Task Broadcasted(PendingTransactionFullId id, Transaction transaction)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var pt = await ctx.PendingTransactions.FirstOrDefaultAsync(p =>
            p.CryptoCode == id.CryptoCode && p.StoreId == id.StoreId && p.Id == id.Id &&
            (p.State == PendingTransactionState.Pending || p.State == PendingTransactionState.Signed));
        if (pt is null) return;
        if (pt.NoSignatureTransactionId != GetNoSignatureHash(transaction).ToString())
            return;
        pt.State = PendingTransactionState.Broadcast;
        pt.TransactionId = transaction.GetHash().ToString();
        await ctx.SaveChangesAsync();
        EventAggregator.Publish(new PendingTransactionEvent
        {
            Data = pt,
            SignerUserId = null,
            Type = PendingTransactionEvent.Broadcast
        });
    }

    private static uint256 GetNoSignatureHash(Transaction transaction)
    {
        var noSignatureTransaction = transaction.Clone();
        noSignatureTransaction.RemoveSignatures();
        return noSignatureTransaction.GetHash();
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

    private static PSBT BuildEffectivePsbt(PendingTransactionBlob blob, Network network)
    {
        var effectivePsbt = PSBT.Parse(blob.PSBT, network);
        foreach (var collectedSignature in blob.CollectedSignatures)
        {
            effectivePsbt.Combine(PSBT.Parse(collectedSignature.ReceivedPSBT, network));
        }

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
                : validExpectedPartialSigCount;
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

    private static string FormatFinalizationErrors(IList<PSBTError>? errors)
    {
        if (errors is null or { Count: 0 })
            return "unknown";

        const int maxErrors = 20;
        const int maxMessageLength = 256;
        var details = errors.Take(maxErrors).Select(error =>
        {
            var message = error.Message.Replace('\r', ' ').Replace('\n', ' ');
            if (message.Length > maxMessageLength)
                message = $"{message[..maxMessageLength]}…";
            return $"input {error.InputIndex}: {message}";
        });
        var result = string.Join(" | ", details);
        return errors.Count > maxErrors
            ? $"{result} | {errors.Count - maxErrors} more error(s)"
            : result;
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
