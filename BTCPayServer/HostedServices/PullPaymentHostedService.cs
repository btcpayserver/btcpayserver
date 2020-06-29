using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using BTCPayServer.Services.Rates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.RPC;

namespace BTCPayServer.HostedServices
{
    public class CreatePullPayment
    {
        public DateTimeOffset? ExpiresAt { get; set; }
        public DateTimeOffset? StartsAt { get; set; }
        public string StoreId { get; set; }
        public string Name { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public PaymentMethodId[] PaymentMethodIds { get; set; }
        public TimeSpan? Period { get; set; }
    }
    public class PullPaymentHostedService : BaseAsyncService
    {
        public class CancelRequest
        {
            public CancelRequest(string pullPaymentId)
            {
                if (pullPaymentId == null)
                    throw new ArgumentNullException(nameof(pullPaymentId));
                PullPaymentId = pullPaymentId;
            }
            public CancelRequest(string[] payoutIds)
            {
                if (payoutIds == null)
                    throw new ArgumentNullException(nameof(payoutIds));
                PayoutIds = payoutIds;
            }
            public string PullPaymentId { get; set; }
            public string[] PayoutIds { get; set; }
            internal TaskCompletionSource<bool> Completion { get; set; }
        }
        public class PayoutApproval
        {
            public enum Result
            {
                Ok,
                NotFound,
                InvalidState,
                TooLowAmount,
                OldRevision
            }
            public string PayoutId { get; set; }
            public int Revision { get; set; }
            public decimal Rate { get; set; }
            internal TaskCompletionSource<Result> Completion { get; set; }

            public static string GetErrorMessage(Result result)
            {
                switch (result)
                {
                    case PullPaymentHostedService.PayoutApproval.Result.Ok:
                        return "Ok";
                    case PullPaymentHostedService.PayoutApproval.Result.InvalidState:
                        return "The payout is not in a state that can be approved";
                    case PullPaymentHostedService.PayoutApproval.Result.TooLowAmount:
                        return "The crypto amount is too small.";
                    case PullPaymentHostedService.PayoutApproval.Result.OldRevision:
                        return "The crypto amount is too small.";
                    case PullPaymentHostedService.PayoutApproval.Result.NotFound:
                        return "The payout is not found";
                    default:
                        throw new NotSupportedException();
                }
            }
        }
        public async Task<string> CreatePullPayment(CreatePullPayment create)
        {
            if (create == null)
                throw new ArgumentNullException(nameof(create));
            if (create.Amount <= 0.0m)
                throw new ArgumentException("Amount out of bound", nameof(create));
            using var ctx = this._dbContextFactory.CreateContext();
            var o = new Data.PullPaymentData();
            o.StartDate = create.StartsAt is DateTimeOffset date ? date : DateTimeOffset.UtcNow;
            o.EndDate = create.ExpiresAt is DateTimeOffset date2 ? new DateTimeOffset?(date2) : null;
            o.Period = create.Period is TimeSpan period ? (long?)period.TotalSeconds : null;
            o.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20));
            o.StoreId = create.StoreId;
            o.SetBlob(new PullPaymentBlob()
            {
                Name = create.Name ?? string.Empty,
                Currency = create.Currency,
                Limit = create.Amount,
                Period = o.Period is long periodSeconds ? (TimeSpan?)TimeSpan.FromSeconds(periodSeconds) : null,
                SupportedPaymentMethods = create.PaymentMethodIds,
                View = new PullPaymentView()
                {
                    Title = create.Name ?? string.Empty,
                    Description = string.Empty,
                    CustomCSSLink = null,
                    Email = null,
                    EmbeddedCSS = null,
                }
            });
            ctx.PullPayments.Add(o);
            await ctx.SaveChangesAsync();
            return o.Id;
        }

        public async Task<Data.PullPaymentData> GetPullPayment(string pullPaymentId)
        {
            using var ctx = _dbContextFactory.CreateContext();
            return await ctx.PullPayments.FindAsync(pullPaymentId);
        }

        class PayoutRequest
        {
            public PayoutRequest(TaskCompletionSource<ClaimRequest.ClaimResponse> completionSource, ClaimRequest request)
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(request));
                if (completionSource == null)
                    throw new ArgumentNullException(nameof(completionSource));
                Completion = completionSource;
                ClaimRequest = request;
            }
            public TaskCompletionSource<ClaimRequest.ClaimResponse> Completion { get; set; }
            public ClaimRequest ClaimRequest { get; }
        }
        public PullPaymentHostedService(ApplicationDbContextFactory dbContextFactory,
            BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
            CurrencyNameTable currencyNameTable,
            EventAggregator eventAggregator,
            ExplorerClientProvider explorerClientProvider,
            BTCPayNetworkProvider networkProvider,
            NotificationSender notificationSender,
            RateFetcher rateFetcher)
        {
            _dbContextFactory = dbContextFactory;
            _jsonSerializerSettings = jsonSerializerSettings;
            _currencyNameTable = currencyNameTable;
            _eventAggregator = eventAggregator;
            _explorerClientProvider = explorerClientProvider;
            _networkProvider = networkProvider;
            _notificationSender = notificationSender;
            _rateFetcher = rateFetcher;
        }

        Channel<object> _Channel;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly EventAggregator _eventAggregator;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly NotificationSender _notificationSender;
        private readonly RateFetcher _rateFetcher;

        internal override Task[] InitializeTasks()
        {
            _Channel = Channel.CreateUnbounded<object>();
            _eventAggregator.Subscribe<NewOnChainTransactionEvent>(o => _Channel.Writer.TryWrite(o));
            _eventAggregator.Subscribe<NewBlockEvent>(o => _Channel.Writer.TryWrite(o));
            return new[] { Loop() };
        }

        private async Task Loop()
        {
            await foreach (var o in _Channel.Reader.ReadAllAsync())
            {
                if (o is PayoutRequest req)
                {
                    await HandleCreatePayout(req);
                }

                if (o is PayoutApproval approv)
                {
                    await HandleApproval(approv);
                }

                if (o is NewOnChainTransactionEvent newTransaction)
                {
                    await UpdatePayoutsAwaitingForPayment(newTransaction);
                }
                if (o is CancelRequest cancel)
                {
                    await HandleCancel(cancel);
                }
                if (o is NewBlockEvent || o is NewOnChainTransactionEvent)
                {
                    await UpdatePayoutsInProgress();
                }
            }
        }

        public Task<RateResult> GetRate(PayoutData payout, string explicitRateRule, CancellationToken cancellationToken)
        {
            var ppBlob = payout.PullPaymentData.GetBlob();
            var currencyPair = new Rating.CurrencyPair(payout.GetPaymentMethodId().CryptoCode, ppBlob.Currency);
            Rating.RateRule rule = null;
            try
            {
                if (explicitRateRule is null)
                {
                    var storeBlob = payout.PullPaymentData.StoreData.GetStoreBlob();
                    var rules = storeBlob.GetRateRules(_networkProvider);
                    rules.Spread = 0.0m;
                    rule = rules.GetRuleFor(currencyPair);
                }
                else
                {
                    rule = Rating.RateRule.CreateFromExpression(explicitRateRule, currencyPair);
                }
            }
            catch (Exception)
            {
                throw new FormatException("Invalid RateRule");
            }
            return _rateFetcher.FetchRate(rule, cancellationToken);
        }
        public Task<PayoutApproval.Result> Approve(PayoutApproval approval)
        {
            approval.Completion = new TaskCompletionSource<PayoutApproval.Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_Channel.Writer.TryWrite(approval))
                throw new ObjectDisposedException(nameof(PullPaymentHostedService));
            return approval.Completion.Task;
        }
        private async Task HandleApproval(PayoutApproval req)
        {
            try
            {
                using var ctx = _dbContextFactory.CreateContext();
                var payout = await ctx.Payouts.Include(p => p.PullPaymentData).Where(p => p.Id == req.PayoutId).FirstOrDefaultAsync();
                if (payout is null)
                {
                    req.Completion.SetResult(PayoutApproval.Result.NotFound);
                    return;
                }
                if (payout.State != PayoutState.AwaitingApproval)
                {
                    req.Completion.SetResult(PayoutApproval.Result.InvalidState);
                    return;
                }
                var payoutBlob = payout.GetBlob(this._jsonSerializerSettings);
                if (payoutBlob.Revision != req.Revision)
                {
                    req.Completion.SetResult(PayoutApproval.Result.OldRevision);
                    return;
                }
                payout.State = PayoutState.AwaitingPayment;
                var paymentMethod = PaymentMethodId.Parse(payout.PaymentMethodId);
                if (paymentMethod.CryptoCode == payout.PullPaymentData.GetBlob().Currency)
                    req.Rate = 1.0m;
                var cryptoAmount = Money.Coins(payoutBlob.Amount / req.Rate);
                Money mininumCryptoAmount = GetMinimumCryptoAmount(paymentMethod, payoutBlob.Destination.Address.ScriptPubKey);
                if (cryptoAmount < mininumCryptoAmount)
                {
                    req.Completion.TrySetResult(PayoutApproval.Result.TooLowAmount);
                    return;
                }
                payoutBlob.CryptoAmount = cryptoAmount.ToDecimal(MoneyUnit.BTC);
                payout.SetBlob(payoutBlob, this._jsonSerializerSettings);
                await ctx.SaveChangesAsync();
                req.Completion.SetResult(PayoutApproval.Result.Ok);
            }
            catch (Exception ex)
            {
                req.Completion.TrySetException(ex);
            }
        }

        private async Task HandleCreatePayout(PayoutRequest req)
        {
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                using var ctx = _dbContextFactory.CreateContext();
                var pp = await ctx.PullPayments.FindAsync(req.ClaimRequest.PullPaymentId);
                if (pp is null || pp.Archived)
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Archived));
                    return;
                }
                if (pp.IsExpired(now))
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Expired));
                    return;
                }
                if (!pp.HasStarted(now))
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.NotStarted));
                    return;
                }
                var ppBlob = pp.GetBlob();
                if (!ppBlob.SupportedPaymentMethods.Contains(req.ClaimRequest.PaymentMethodId))
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.PaymentMethodNotSupported));
                    return;
                }
                var payouts = (await ctx.Payouts.GetPayoutInPeriod(pp, now)
                                                .Where(p => p.State != PayoutState.Cancelled)
                                                .ToListAsync())
                              .Select(o => new
                              {
                                  Entity = o,
                                  Blob = o.GetBlob(_jsonSerializerSettings)
                              });
                var cd = _currencyNameTable.GetCurrencyData(pp.GetBlob().Currency, false);
                var limit = ppBlob.Limit;
                var totalPayout = payouts.Select(p => p.Blob.Amount).Sum();
                var claimed = req.ClaimRequest.Value is decimal v ? v : limit - totalPayout;
                if (totalPayout + claimed > limit)
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Overdraft));
                    return;
                }
                var payout = new PayoutData()
                {
                    Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20)),
                    Date = now,
                    State = PayoutState.AwaitingApproval,
                    PullPaymentDataId = req.ClaimRequest.PullPaymentId,
                    PaymentMethodId = req.ClaimRequest.PaymentMethodId.ToString(),
                    Destination = GetDestination(req.ClaimRequest.Destination.Address.ScriptPubKey)
                };
                if (claimed < ppBlob.MinimumClaim || claimed == 0.0m)
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.AmountTooLow));
                    return;
                }
                var payoutBlob = new PayoutBlob()
                {
                    Amount = claimed,
                    Destination = req.ClaimRequest.Destination
                };
                payout.SetBlob(payoutBlob, _jsonSerializerSettings);
                payout.SetProofBlob(new PayoutTransactionOnChainBlob(), _jsonSerializerSettings);
                ctx.Payouts.Add(payout);
                try
                {
                    await ctx.SaveChangesAsync();
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Ok, payout));
                    await _notificationSender.SendNotification(new StoreScope(pp.StoreId), new PayoutNotification()
                    {
                        StoreId = pp.StoreId,
                        Currency = ppBlob.Currency,
                        PaymentMethod = payout.PaymentMethodId,
                        PayoutId = pp.Id
                    });
                }
                catch (DbUpdateException)
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Duplicate));
                }
            }
            catch (Exception ex)
            {
                req.Completion.TrySetException(ex);
            }
        }

        private async Task UpdatePayoutsAwaitingForPayment(NewOnChainTransactionEvent newTransaction)
        {
            try
            {
                var outputs = newTransaction.
                    NewTransactionEvent.
                    TransactionData.
                    Transaction.
                    Outputs;
                var destinations = outputs.Select(o => GetDestination(o.ScriptPubKey)).ToHashSet();

                using var ctx = _dbContextFactory.CreateContext();
                var payouts = await ctx.Payouts
                    .Include(o => o.PullPaymentData)
                    .Where(p => p.State == PayoutState.AwaitingPayment)
                    .Where(p => destinations.Contains(p.Destination))
                    .ToListAsync();
                var payoutByDestination = payouts.ToDictionary(p => p.Destination);
                foreach (var output in outputs)
                {
                    if (!payoutByDestination.TryGetValue(GetDestination(output.ScriptPubKey), out var payout))
                        continue;
                    var payoutBlob = payout.GetBlob(this._jsonSerializerSettings);
                    if (output.Value.ToDecimal(MoneyUnit.BTC) != payoutBlob.CryptoAmount)
                        continue;
                    var proof = payout.GetProofBlob(this._jsonSerializerSettings);
                    var txId = newTransaction.NewTransactionEvent.TransactionData.TransactionHash;
                    if (proof.Candidates.Add(txId))
                    {
                        payout.State = PayoutState.InProgress;
                        if (proof.TransactionId is null)
                            proof.TransactionId = txId;
                        payout.SetProofBlob(proof, _jsonSerializerSettings);
                        _eventAggregator.Publish(new UpdateTransactionLabel(new WalletId(payout.PullPaymentData.StoreId, newTransaction.CryptoCode),
                                                                        newTransaction.NewTransactionEvent.TransactionData.TransactionHash,
                                                                        ("#3F88AF", "Payout")));
                    }
                }
                await ctx.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogWarning(ex, "Error while processing a transaction in the pull payment hosted service");
            }
        }

        private async Task HandleCancel(CancelRequest cancel)
        {
            try
            {
                using var ctx = this._dbContextFactory.CreateContext();
                List<PayoutData> payouts = null;
                if (cancel.PullPaymentId != null)
                {
                    ctx.PullPayments.Attach(new Data.PullPaymentData() { Id = cancel.PullPaymentId, Archived = true })
                        .Property(o => o.Archived).IsModified = true;
                    payouts = await ctx.Payouts
                            .Where(p => p.PullPaymentDataId == cancel.PullPaymentId)
                            .ToListAsync();
                }
                else
                {
                    var payoutIds = cancel.PayoutIds.ToHashSet();
                    payouts = await ctx.Payouts
                            .Where(p => payoutIds.Contains(p.Id))
                            .ToListAsync();
                }

                foreach (var payout in payouts)
                {
                    if (payout.State != PayoutState.Completed && payout.State != PayoutState.InProgress)
                        payout.State = PayoutState.Cancelled;
                    payout.Destination = null;
                }
                await ctx.SaveChangesAsync();
                cancel.Completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                cancel.Completion.TrySetException(ex);
            }
        }

        private async Task UpdatePayoutsInProgress()
        {
            try
            {
                using var ctx = _dbContextFactory.CreateContext();
                var payouts = await ctx.Payouts
                            .Include(p => p.PullPaymentData)
                            .Where(p => p.State == PayoutState.InProgress)
                            .ToListAsync();

                foreach (var payout in payouts)
                {
                    var proof = payout.GetProofBlob(this._jsonSerializerSettings);
                    var payoutBlob = payout.GetBlob(this._jsonSerializerSettings);
                    foreach (var txid in proof.Candidates.ToList())
                    {
                        var explorer = _explorerClientProvider.GetExplorerClient(payout.GetPaymentMethodId().CryptoCode);
                        var tx = await explorer.GetTransactionAsync(txid);
                        if (tx is null)
                        {
                            proof.Candidates.Remove(txid);
                        }
                        else if (tx.Confirmations >= payoutBlob.MinimumConfirmation)
                        {
                            payout.State = PayoutState.Completed;
                            proof.TransactionId = tx.TransactionHash;
                            payout.Destination = null;
                            break;
                        }
                        else
                        {
                            var rebroadcasted = await explorer.BroadcastAsync(tx.Transaction);
                            if (rebroadcasted.RPCCode == RPCErrorCode.RPC_TRANSACTION_ERROR ||
                                rebroadcasted.RPCCode == RPCErrorCode.RPC_TRANSACTION_REJECTED)
                            {
                                proof.Candidates.Remove(txid);
                            }
                            else
                            {
                                payout.State = PayoutState.InProgress;
                                proof.TransactionId = tx.TransactionHash;
                                continue;
                            }
                        }
                    }
                    if (proof.TransactionId is null && !proof.Candidates.Contains(proof.TransactionId))
                    {
                        proof.TransactionId = null;
                    }
                    if (proof.Candidates.Count == 0)
                    {
                        payout.State = PayoutState.AwaitingPayment;
                    }
                    else if (proof.TransactionId is null)
                    {
                        proof.TransactionId = proof.Candidates.First();
                    }
                    if (payout.State == PayoutState.Completed)
                        proof.Candidates = null;
                    payout.SetProofBlob(proof, this._jsonSerializerSettings);
                }
                await ctx.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogWarning(ex, "Error while processing an update in the pull payment hosted service");
            }
        }

        private Money GetMinimumCryptoAmount(PaymentMethodId paymentMethodId, Script scriptPubKey)
        {
            Money mininumAmount = Money.Zero;
            if (_networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode)?
                            .NBitcoinNetwork?
                            .Consensus?
                            .ConsensusFactory?
                            .CreateTxOut() is TxOut txout)
            {
                txout.ScriptPubKey = scriptPubKey;
                mininumAmount = txout.GetDustThreshold(new FeeRate(1.0m));
            }
            return mininumAmount;
        }

        private static string GetDestination(Script scriptPubKey)
        {
            return Encoders.Base64.EncodeData(scriptPubKey.ToBytes(true));
        }
        public Task Cancel(CancelRequest cancelRequest)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var cts = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancelRequest.Completion = cts;
            if (!_Channel.Writer.TryWrite(cancelRequest))
                throw new ObjectDisposedException(nameof(PullPaymentHostedService));
            return cts.Task;
        }

        public Task<ClaimRequest.ClaimResponse> Claim(ClaimRequest request)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var cts = new TaskCompletionSource<ClaimRequest.ClaimResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_Channel.Writer.TryWrite(new PayoutRequest(cts, request)))
                throw new ObjectDisposedException(nameof(PullPaymentHostedService));
            return cts.Task;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _Channel?.Writer.Complete();
            return base.StopAsync(cancellationToken);
        }
    }

    public class ClaimRequest
    {
        public static string GetErrorMessage(ClaimResult result)
        {
            switch (result)
            {
                case ClaimResult.Ok:
                    break;
                case ClaimResult.Duplicate:
                    return "This address is already used for another payout";
                case ClaimResult.Expired:
                    return "This pull payment is expired";
                case ClaimResult.NotStarted:
                    return "This pull payment has yet started";
                case ClaimResult.Archived:
                    return "This pull payment has been archived";
                case ClaimResult.Overdraft:
                    return "The payout amount overdraft the pull payment's limit";
                case ClaimResult.AmountTooLow:
                    return "The requested payout amount is too low";
                case ClaimResult.PaymentMethodNotSupported:
                    return "This payment method is not supported by the pull payment";
                default:
                    throw new NotSupportedException("Unsupported ClaimResult");
            }
            return null;
        }
        public class ClaimResponse
        {
            public ClaimResponse(ClaimResult result, PayoutData payoutData = null)
            {
                Result = result;
                PayoutData = payoutData;
            }
            public ClaimResult Result { get; set; }
            public PayoutData PayoutData { get; set; }
        }
        public enum ClaimResult
        {
            Ok,
            Duplicate,
            Expired,
            Archived,
            NotStarted,
            Overdraft,
            AmountTooLow,
            PaymentMethodNotSupported,
        }

        public PaymentMethodId PaymentMethodId { get; set; }
        public string PullPaymentId { get; set; }
        public decimal? Value { get; set; }
        public IClaimDestination Destination { get; set; }
    }

}
