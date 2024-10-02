using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using BTCPayServer.Services.Rates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;
using Newtonsoft.Json.Linq;
using PayoutData = BTCPayServer.Data.PayoutData;
using PullPaymentData = BTCPayServer.Data.PullPaymentData;


namespace BTCPayServer.HostedServices
{
    public class CreatePullPayment
    {
        public DateTimeOffset? ExpiresAt { get; set; }
        public DateTimeOffset? StartsAt { get; set; }
        public string StoreId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public PayoutMethodId[] PayoutMethods { get; set; }
        public bool AutoApproveClaims { get; set; }
        public TimeSpan? BOLT11Expiration { get; set; }
    }

    public class PullPaymentHostedService : BaseAsyncService
    {
        private readonly string[] _lnurlSupportedCurrencies = { "BTC", "SATS" };

        public class CancelRequest
        {
            public CancelRequest(string pullPaymentId)
            {
                ArgumentNullException.ThrowIfNull(pullPaymentId);
                PullPaymentId = pullPaymentId;
            }

            public CancelRequest(string[] payoutIds, string[] storeIds)
            {
                ArgumentNullException.ThrowIfNull(payoutIds);
                PayoutIds = payoutIds;
                StoreIds = storeIds;
            }

            public string[] StoreIds { get; set; }

            public string PullPaymentId { get; set; }
            public string[] PayoutIds { get; set; }
            internal TaskCompletionSource<Dictionary<string, MarkPayoutRequest.PayoutPaidResult>> Completion { get; set; }
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

            public record ApprovalResult(Result Result, decimal? CryptoAmount);

            public string PayoutId { get; set; }
            public int Revision { get; set; }
            public decimal Rate { get; set; }
            internal TaskCompletionSource<ApprovalResult> Completion { get; set; }

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
        public Task<string> CreatePullPayment(string storeId, CreatePullPaymentRequest request)
        {
            return CreatePullPayment(new CreatePullPayment()
            {
                StartsAt = request.StartsAt,
                ExpiresAt = request.ExpiresAt,
                BOLT11Expiration = request.BOLT11Expiration,
                Name = request.Name,
                Description = request.Description,
                Amount = request.Amount,
                Currency = request.Currency,
                StoreId = storeId,
                PayoutMethods = request.PayoutMethods.Select(p => PayoutMethodId.Parse(p)).ToArray(),
                AutoApproveClaims = request.AutoApproveClaims
            });
        }
        public async Task<string> CreatePullPayment(CreatePullPayment create)
        {
            ArgumentNullException.ThrowIfNull(create);
            if (create.Amount <= 0.0m)
                throw new ArgumentException("Amount out of bound", nameof(create));
            using var ctx = this._dbContextFactory.CreateContext();
            var o = new Data.PullPaymentData();
            o.StartDate = create.StartsAt is DateTimeOffset date
                ? date
                : DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1.0);
            o.EndDate = create.ExpiresAt is DateTimeOffset date2 ? new DateTimeOffset?(date2) : null;
            o.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20));
            o.StoreId = create.StoreId;
            o.Currency = create.Currency;
            o.Limit = create.Amount;

            o.SetBlob(new PullPaymentBlob()
            {
                Name = create.Name ?? string.Empty,
                Description = create.Description ?? string.Empty,
                SupportedPayoutMethods = create.PayoutMethods,
                AutoApproveClaims = create.AutoApproveClaims,
                View = new PullPaymentBlob.PullPaymentView
                {
                    Title = create.Name ?? string.Empty,
                    Description = create.Description ?? string.Empty,
                    Email = null
                },
                BOLT11Expiration = create.BOLT11Expiration ?? TimeSpan.FromDays(30.0)
            });
            ctx.PullPayments.Add(o);
            await ctx.SaveChangesAsync();
            return o.Id;
        }

        public class PayoutQuery
        {
            public PayoutState[] States { get; set; }
            public string[] PullPayments { get; set; }
            public string[] PayoutIds { get; set; }
            public string[] PayoutMethods { get; set; }
            public string[] Stores { get; set; }
            public bool IncludeArchived { get; set; }
            public bool IncludeStoreData { get; set; }
            public bool IncludePullPaymentData { get; set; }
            public DateTimeOffset? From { get; set; }
            public DateTimeOffset? To { get; set; }
        }

        public async Task<List<PayoutData>> GetPayouts(PayoutQuery payoutQuery)
        {
            await using var ctx = _dbContextFactory.CreateContext();
            return await GetPayouts(payoutQuery, ctx);
        }

        public static async Task<List<PayoutData>> GetPayouts(PayoutQuery payoutQuery, ApplicationDbContext ctx,
            CancellationToken cancellationToken = default)
        {
            var query = ctx.Payouts.AsQueryable();
            if (payoutQuery.States is not null)
            {
                if (payoutQuery.States.Length == 1)
                {
                    var state = payoutQuery.States[0];
                    query = query.Where(data => data.State == state);
                }
                else
                {
                    query = query.Where(data => payoutQuery.States.Contains(data.State));
                }
            }

            if (payoutQuery.PullPayments is not null)
            {
                query = query.Where(data => payoutQuery.PullPayments.Contains(data.PullPaymentDataId));
            }

            if (payoutQuery.PayoutIds is not null)
            {
                if (payoutQuery.PayoutIds.Length == 1)
                {
                    var payoutId = payoutQuery.PayoutIds[0];
                    query = query.Where(data => data.Id == payoutId);
                }
                else
                {
                    query = query.Where(data => payoutQuery.PayoutIds.Contains(data.Id));
                }
            }

            if (payoutQuery.PayoutMethods is not null)
            {
                if (payoutQuery.PayoutMethods.Length == 1)
                {
                    var pm = payoutQuery.PayoutMethods[0];
                    query = query.Where(data => pm == data.PayoutMethodId);
                }
                else
                {
                    query = query.Where(data => payoutQuery.PayoutMethods.Contains(data.PayoutMethodId));
                }
            }

            if (payoutQuery.Stores is not null)
            {
                if (payoutQuery.Stores.Length == 1)
                {
                    var store = payoutQuery.Stores[0];
                    query = query.Where(data => store == data.StoreDataId);
                }
                else
                {
                    query = query.Where(data => payoutQuery.Stores.Contains(data.StoreDataId));
                }
            }
            if (payoutQuery.IncludeStoreData)
            {
                query = query.Include(data => data.StoreData);
            }

            if (payoutQuery.IncludePullPaymentData || !payoutQuery.IncludeArchived)
            {
                query = query.Include(data => data.PullPaymentData);
            }

            if (!payoutQuery.IncludeArchived)
            {
                query = query.Where(data =>
                    data.PullPaymentData == null || !data.PullPaymentData.Archived);
            }

            if (payoutQuery.From is not null)
            {
                query = query.Where(data => data.Date >= payoutQuery.From);
            }
            if (payoutQuery.To is not null)
            {
                query = query.Where(data => data.Date <= payoutQuery.To);
            }
            return await query.ToListAsync(cancellationToken);
        }

        public async Task<Data.PullPaymentData> GetPullPayment(string pullPaymentId, bool includePayouts)
        {
            await using var ctx = _dbContextFactory.CreateContext();
            IQueryable<Data.PullPaymentData> query = ctx.PullPayments;
            if (includePayouts)
                query = query.Include(data => data.Payouts);

            return await query.FirstOrDefaultAsync(data => data.Id == pullPaymentId);
        }
        record TopUpRequest(string PullPaymentId, InvoiceEntity InvoiceEntity);
        class PayoutRequest
        {
            public PayoutRequest(TaskCompletionSource<ClaimRequest.ClaimResponse> completionSource,
                ClaimRequest request)
            {
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(completionSource);
                Completion = completionSource;
                ClaimRequest = request;
            }

            public TaskCompletionSource<ClaimRequest.ClaimResponse> Completion { get; set; }
            public ClaimRequest ClaimRequest { get; }
        }

        public PullPaymentHostedService(ApplicationDbContextFactory dbContextFactory,
            BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider networkProvider,
            PayoutMethodHandlerDictionary handlers,
            DefaultRulesCollection defaultRules,
            NotificationSender notificationSender,
            RateFetcher rateFetcher,
            ILogger<PullPaymentHostedService> logger,
            Logs logs,
            DisplayFormatter displayFormatter,
            CurrencyNameTable currencyNameTable) : base(logs)
        {
            _dbContextFactory = dbContextFactory;
            _jsonSerializerSettings = jsonSerializerSettings;
            _eventAggregator = eventAggregator;
            _networkProvider = networkProvider;
            _handlers = handlers;
            _defaultRules = defaultRules;
            _notificationSender = notificationSender;
            _rateFetcher = rateFetcher;
            _logger = logger;
            _currencyNameTable = currencyNameTable;
            _displayFormatter = displayFormatter;
        }

        Channel<object> _Channel;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly PayoutMethodHandlerDictionary _handlers;
        private readonly DefaultRulesCollection _defaultRules;
        private readonly NotificationSender _notificationSender;
        private readonly RateFetcher _rateFetcher;
        private readonly ILogger<PullPaymentHostedService> _logger;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly DisplayFormatter _displayFormatter;
        private readonly CompositeDisposable _subscriptions = new CompositeDisposable();

        internal override Task[] InitializeTasks()
        {
            _Channel = Channel.CreateUnbounded<object>();
            foreach (IPayoutHandler payoutHandler in _handlers)
            {
                payoutHandler.StartBackgroundCheck(Subscribe);
            }
            _eventAggregator.Subscribe<Events.InvoiceEvent>(TopUpInvoiceCore);
            return new[] { Loop() };
        }

        private void TopUpInvoiceCore(InvoiceEvent evt)
        {
            if (evt.EventCode == InvoiceEventCode.Completed || evt.EventCode == InvoiceEventCode.MarkedCompleted)
            {
                foreach (var pullPaymentId in evt.Invoice.GetInternalTags("PULLPAY#"))
                {
                    _Channel.Writer.TryWrite(new TopUpRequest(pullPaymentId, evt.Invoice));
                }
            }
        }

        private void Subscribe(params Type[] events)
        {
            foreach (Type @event in events)
            {
                _eventAggregator.Subscribe(@event, (subscription, o) => _Channel.Writer.TryWrite(o));
            }
        }

        private async Task Loop()
        {
            await foreach (var o in _Channel.Reader.ReadAllAsync())
            {
                if (o is TopUpRequest topUp)
                {
                    await HandleTopUp(topUp);
                }

                if (o is PayoutRequest req)
                {
                    await HandleCreatePayout(req);
                }

                if (o is PayoutApproval approv)
                {
                    await HandleApproval(approv);
                }

                if (o is CancelRequest cancel)
                {
                    await HandleCancel(cancel);
                }

                if (o is InternalPayoutPaidRequest paid)
                {
                    await HandleMarkPaid(paid);
                }

                foreach (IPayoutHandler payoutHandler in _handlers)
                {
                    try
                    {
                        await payoutHandler.BackgroundCheck(o);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "PayoutHandler failed during BackgroundCheck");
                    }
                }
            }
        }

        private async Task HandleTopUp(TopUpRequest topUp)
        {
            var pp = await this.GetPullPayment(topUp.PullPaymentId, false);
            using var ctx = _dbContextFactory.CreateContext();

            var payout = new Data.PayoutData()
            {
                Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20)),
                PayoutMethodId = PayoutMethodIds.TopUp.ToString(),
                Date = DateTimeOffset.UtcNow,
                State = PayoutState.Completed,
                PullPaymentDataId = pp.Id,
                StoreDataId = pp.StoreId
            };
            if (topUp.InvoiceEntity.Currency != pp.Currency ||
                pp.Currency is not ("SATS" or "BTC"))
                return;
            payout.Currency = pp.Currency;
            payout.Amount = -topUp.InvoiceEntity.Price;
            payout.OriginalCurrency = payout.Currency;
            payout.OriginalAmount = payout.Amount.Value;
            var payoutBlob = new PayoutBlob()
            {
                Destination = topUp.InvoiceEntity.Id,
                Metadata = new JObject()
            };
            payout.SetBlob(payoutBlob, _jsonSerializerSettings);
            await ctx.Payouts.AddAsync(payout);
            await ctx.SaveChangesAsync();
        }

        public bool SupportsLNURL(PullPaymentData pp, PullPaymentBlob blob = null)
        {
            blob ??= pp.GetBlob();
            var pms = blob.SupportedPayoutMethods.FirstOrDefault(id =>
                PayoutTypes.LN.GetPayoutMethodId(_networkProvider.DefaultNetwork.CryptoCode)
                == id);
            return pms is not null && _lnurlSupportedCurrencies.Contains(pp.Currency);
        }

        public Task<RateResult> GetRate(PayoutData payout, string explicitRateRule, CancellationToken cancellationToken)
        {
            var payoutPaymentMethod = payout.GetPayoutMethodId();
            var cryptoCode = _handlers.TryGetNetwork(payoutPaymentMethod)?.NBXplorerNetwork.CryptoCode;
            var currencyPair = new Rating.CurrencyPair(cryptoCode,
                payout.PullPaymentData?.Currency ?? cryptoCode);
            Rating.RateRule rule = null;
            try
            {
                if (explicitRateRule is null)
                {
                    var storeBlob = payout.StoreData.GetStoreBlob();
                    var rules = storeBlob.GetRateRules(_defaultRules);
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

            return _rateFetcher.FetchRate(rule, new StoreIdRateContext(payout.StoreDataId), cancellationToken);
        }

        public Task<PayoutApproval.ApprovalResult> Approve(PayoutApproval approval)
        {
            approval.Completion =
                new TaskCompletionSource<PayoutApproval.ApprovalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_Channel.Writer.TryWrite(approval))
                throw new ObjectDisposedException(nameof(PullPaymentHostedService));
            return approval.Completion.Task;
        }

        private async Task HandleApproval(PayoutApproval req)
        {
            try
            {
                await using var ctx = _dbContextFactory.CreateContext();
                var payout = await ctx.Payouts.Include(p => p.PullPaymentData).Where(p => p.Id == req.PayoutId)
                    .FirstOrDefaultAsync();
                if (payout is null)
                {
                    req.Completion.SetResult(new PayoutApproval.ApprovalResult(PayoutApproval.Result.NotFound, null));
                    return;
                }

                if (payout.State != PayoutState.AwaitingApproval)
                {
                    req.Completion.SetResult(new PayoutApproval.ApprovalResult(PayoutApproval.Result.InvalidState, null));
                    return;
                }

                var payoutBlob = payout.GetBlob(this._jsonSerializerSettings);
                if (payoutBlob.Revision != req.Revision)
                {
                    req.Completion.SetResult(new PayoutApproval.ApprovalResult(PayoutApproval.Result.OldRevision, null));
                    return;
                }

                if (!PayoutMethodId.TryParse(payout.PayoutMethodId, out var paymentMethod))
                {
                    req.Completion.SetResult(new PayoutApproval.ApprovalResult(PayoutApproval.Result.NotFound, null));
                    return;
                }
                var network = _handlers.TryGetNetwork(paymentMethod);
                if (network is null)
                {
                    req.Completion.SetResult(new PayoutApproval.ApprovalResult(PayoutApproval.Result.InvalidState, null));
                    return;
                }
                var cryptoCode = network.NBXplorerNetwork.CryptoCode;
                payout.State = PayoutState.AwaitingPayment;

                if (payout.PullPaymentData is null ||
                    cryptoCode == payout.PullPaymentData.Currency)
                    req.Rate = 1.0m;
                var cryptoAmount = payout.OriginalAmount / req.Rate;
                var payoutHandler = _handlers.TryGet(paymentMethod);
                if (payoutHandler is null)
                    throw new InvalidOperationException($"No payout handler for {paymentMethod}");
                var dest = await payoutHandler.ParseClaimDestination(payoutBlob.Destination, default);
                decimal minimumCryptoAmount =
                    await payoutHandler.GetMinimumPayoutAmount(dest.destination);
                if (cryptoAmount < minimumCryptoAmount)
                {
                    req.Completion.TrySetResult(new PayoutApproval.ApprovalResult(PayoutApproval.Result.TooLowAmount, null));
                    return;
                }

                payout.Amount = Extensions.RoundUp(cryptoAmount,
                    network.Divisibility);
                await ctx.SaveChangesAsync();

                _eventAggregator.Publish(new PayoutEvent(PayoutEvent.PayoutEventType.Approved, payout));
                req.Completion.SetResult(new PayoutApproval.ApprovalResult(PayoutApproval.Result.Ok, payout.Amount));
            }
            catch (Exception ex)
            {
                req.Completion.TrySetException(ex);
            }
        }

        private async Task HandleMarkPaid(InternalPayoutPaidRequest req)
        {
            try
            {
                await using var ctx = _dbContextFactory.CreateContext();
                var payout = await ctx.Payouts.Include(p => p.PullPaymentData).Where(p => p.Id == req.Request.PayoutId)
                    .FirstOrDefaultAsync();
                if (payout is null)
                {
                    req.Completion.SetResult(MarkPayoutRequest.PayoutPaidResult.NotFound);
                    return;
                }

                if (payout.State == PayoutState.Completed)
                {
                    req.Completion.SetResult(MarkPayoutRequest.PayoutPaidResult.InvalidState);
                    return;
                }
                switch (req.Request.State)
                {
                    case PayoutState.Completed or PayoutState.InProgress
                        when payout.State is not PayoutState.AwaitingPayment and not PayoutState.Completed and not PayoutState.InProgress:
                    case PayoutState.AwaitingPayment when payout.State is not PayoutState.InProgress:
                        req.Completion.SetResult(MarkPayoutRequest.PayoutPaidResult.InvalidState);
                        return;
                    case PayoutState.InProgress or PayoutState.Completed:
                        payout.SetProofBlob(req.Request.Proof);
                        break;
                    default:
                        payout.SetProofBlob(null);
                        break;
                }
                payout.State = req.Request.State;
                await ctx.SaveChangesAsync();
                _eventAggregator.Publish(new PayoutEvent(PayoutEvent.PayoutEventType.Updated, payout));
                req.Completion.SetResult(MarkPayoutRequest.PayoutPaidResult.Ok);
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
                await using var ctx = _dbContextFactory.CreateContext();
                var withoutPullPayment = req.ClaimRequest.PullPaymentId is null;
                var pp = string.IsNullOrEmpty(req.ClaimRequest.PullPaymentId)
                    ? null
                    : await ctx.PullPayments.FindAsync(req.ClaimRequest.PullPaymentId);

                if (!withoutPullPayment && (pp is null || pp.Archived))
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Archived));
                    return;
                }

                PullPaymentBlob ppBlob = null;
                if (!withoutPullPayment)
                {
                    if (pp.IsExpired(now))
                    {
                        req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Expired));
                        return;
                    }

                    if (!pp.HasStarted(now))
                    {
                        req.Completion.TrySetResult(
                            new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.NotStarted));
                        return;
                    }

                    ppBlob = pp.GetBlob();

                    if (!ppBlob.SupportedPayoutMethods.Contains(req.ClaimRequest.PayoutMethodId))
                    {
                        req.Completion.TrySetResult(
                            new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.PaymentMethodNotSupported));
                        return;
                    }
                }

                var payoutHandler =
                    _handlers.TryGet(req.ClaimRequest.PayoutMethodId);
                if (payoutHandler is null)
                {
                    req.Completion.TrySetResult(
                        new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.PaymentMethodNotSupported));
                    return;
                }

                if (req.ClaimRequest.Destination.Id != null)
                {
                    if (await ctx.Payouts.AnyAsync(data =>
                            data.DedupId.Equals(req.ClaimRequest.Destination.Id) &&
                            data.State != PayoutState.Completed && data.State != PayoutState.Cancelled
                        ))
                    {
                        req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Duplicate));
                        return;
                    }
                }

                if (req.ClaimRequest.Value <
                    await payoutHandler.GetMinimumPayoutAmount(req.ClaimRequest.Destination))
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.AmountTooLow));
                    return;
                }

                var payoutsRaw = withoutPullPayment
                    ? null
                    : await ctx.Payouts.Where(p => p.PullPaymentDataId == pp.Id)
                        .Where(p => p.State != PayoutState.Cancelled).ToListAsync();

                var payouts = payoutsRaw?.Select(o => new { Entity = o, Blob = o.GetBlob(_jsonSerializerSettings) });
                var limit = pp?.Limit ?? 0;
                var totalPayout = payouts?.Select(p => p.Entity.OriginalAmount)?.Sum();
                var claimed = req.ClaimRequest.Value is decimal v ? v : limit - (totalPayout ?? 0);
                if (totalPayout is not null && totalPayout + claimed > limit)
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Overdraft));
                    return;
                }

                if (!withoutPullPayment && (claimed < ppBlob.MinimumClaim || claimed == 0.0m))
                {
                    req.Completion.TrySetResult(new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.AmountTooLow));
                    return;
                }

                var payout = new PayoutData()
                {
                    Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(20)),
                    Date = now,
                    State = PayoutState.AwaitingApproval,
                    PullPaymentDataId = req.ClaimRequest.PullPaymentId,
                    PayoutMethodId = req.ClaimRequest.PayoutMethodId.ToString(),
                    DedupId = req.ClaimRequest.Destination.Id,
                    StoreDataId = req.ClaimRequest.StoreId ?? pp?.StoreId,
                    Currency = payoutHandler.Currency,
                    OriginalCurrency = pp?.Currency ?? payoutHandler.Currency
                };
                var payoutBlob = new PayoutBlob()
                {
                    Destination = req.ClaimRequest.Destination.ToString(),
                    Metadata = req.ClaimRequest.Metadata ?? new JObject(),
                };
                payout.OriginalAmount = claimed;
                payout.SetBlob(payoutBlob, _jsonSerializerSettings);
                await ctx.Payouts.AddAsync(payout);
                try
                {
                    await payoutHandler.TrackClaim(req.ClaimRequest, payout);
                    await ctx.SaveChangesAsync();
                    var response = new ClaimRequest.ClaimResponse(ClaimRequest.ClaimResult.Ok, payout);
                    _eventAggregator.Publish(new PayoutEvent(PayoutEvent.PayoutEventType.Created, payout));
                    if (req.ClaimRequest.PreApprove.GetValueOrDefault(ppBlob?.AutoApproveClaims is true))
                    {
                        payout.StoreData = await ctx.Stores.FindAsync(payout.StoreDataId);
                        var rateResult = await GetRate(payout, null, CancellationToken.None);
                        if (rateResult.BidAsk != null)
                        {
                            var approveResultTask = new TaskCompletionSource<PayoutApproval.ApprovalResult>();
                            await HandleApproval(new PayoutApproval()
                            {
                                PayoutId = payout.Id,
                                Revision = payoutBlob.Revision,
                                Rate = rateResult.BidAsk.Ask,
                                Completion = approveResultTask
                            });
                            var approveResult = await approveResultTask.Task;
                            if (approveResult.Result == PayoutApproval.Result.Ok)
                            {
                                payout.State = PayoutState.AwaitingPayment;
                                payout.Amount = approveResult.CryptoAmount;
                            }
                        }
                    }

                    req.Completion.TrySetResult(response);
                    await _notificationSender.SendNotification(new StoreScope(payout.StoreDataId),
                        new PayoutNotification()
                        {
                            StoreId = payout.StoreDataId,
                            Currency = pp?.Currency ?? _handlers.TryGetNetwork(req.ClaimRequest.PayoutMethodId)?.NBXplorerNetwork.CryptoCode,
                            Status = payout.State,
                            PaymentMethod = payout.PayoutMethodId,
                            PayoutId = payout.Id
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
                        .Where(p => cancel.StoreIds == null || cancel.StoreIds.Contains(p.StoreDataId))
                        .ToListAsync();

                    cancel.PayoutIds = payouts.Select(data => data.Id).ToArray();
                }
                else
                {
                    var payoutIds = cancel.PayoutIds.ToHashSet();
                    payouts = await ctx.Payouts
                        .Where(p => payoutIds.Contains(p.Id))
                        .Where(p => cancel.StoreIds == null || cancel.StoreIds.Contains(p.StoreDataId))
                        .ToListAsync();
                }

                Dictionary<string, MarkPayoutRequest.PayoutPaidResult> result = new();

                foreach (var payout in payouts)
                {
                    if (payout.State != PayoutState.Completed && payout.State != PayoutState.InProgress)
                    {
                        payout.State = PayoutState.Cancelled;
                        result.Add(payout.Id, MarkPayoutRequest.PayoutPaidResult.Ok);
                    }
                    else
                    {
                        result.Add(payout.Id, MarkPayoutRequest.PayoutPaidResult.InvalidState);
                    }
                }

                foreach (string s1 in cancel.PayoutIds.Where(s => !result.ContainsKey(s)))
                {
                    result.Add(s1, MarkPayoutRequest.PayoutPaidResult.NotFound);
                }

                await ctx.SaveChangesAsync();
                foreach (var keyValuePair in result.Where(pair => pair.Value == MarkPayoutRequest.PayoutPaidResult.Ok))
                {
                    var payout = payouts.First(p => p.Id == keyValuePair.Key);
                    _eventAggregator.Publish(new PayoutEvent(PayoutEvent.PayoutEventType.Updated, payout));
                }
                cancel.Completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                cancel.Completion.TrySetException(ex);
            }
        }

        public Task<Dictionary<string, MarkPayoutRequest.PayoutPaidResult>> Cancel(CancelRequest cancelRequest)
        {
            CancellationToken.ThrowIfCancellationRequested();
            cancelRequest.Completion = new TaskCompletionSource<Dictionary<string, MarkPayoutRequest.PayoutPaidResult>>();
            if (!_Channel.Writer.TryWrite(cancelRequest))
                throw new ObjectDisposedException(nameof(PullPaymentHostedService));
            return cancelRequest.Completion.Task;
        }

        public Task<ClaimRequest.ClaimResponse> Claim(ClaimRequest request)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var cts = new TaskCompletionSource<ClaimRequest.ClaimResponse>(TaskCreationOptions
                .RunContinuationsAsynchronously);
            if (!_Channel.Writer.TryWrite(new PayoutRequest(cts, request)))
                throw new ObjectDisposedException(nameof(PullPaymentHostedService));
            return cts.Task;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _Channel?.Writer.Complete();
            _subscriptions.Dispose();
            return base.StopAsync(cancellationToken);
        }

        public Task<MarkPayoutRequest.PayoutPaidResult> MarkPaid(MarkPayoutRequest request)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var cts = new TaskCompletionSource<MarkPayoutRequest.PayoutPaidResult>(TaskCreationOptions
                .RunContinuationsAsynchronously);
            if (!_Channel.Writer.TryWrite(new InternalPayoutPaidRequest(cts, request)))
                throw new ObjectDisposedException(nameof(PullPaymentHostedService));
            return cts.Task;
        }


        public PullPaymentsModel.PullPaymentModel.ProgressModel CalculatePullPaymentProgress(PullPaymentData pp,
            DateTimeOffset now)
        {
            var ni = _currencyNameTable.GetCurrencyData(pp.Currency, true);
            var nfi = _currencyNameTable.GetNumberFormatInfo(pp.Currency, true);
            var totalCompleted = pp.Payouts
                .Where(p => (p.State == PayoutState.Completed ||
                                                        p.State == PayoutState.InProgress))
                .Select(o => o.OriginalAmount).Sum().RoundToSignificant(ni.Divisibility);
            var totalAwaiting = pp.Payouts
                .Where(p => (p.State == PayoutState.AwaitingPayment ||
                                                       p.State == PayoutState.AwaitingApproval)).Select(o =>
                o.OriginalAmount).Sum().RoundToSignificant(ni.Divisibility);

            var currencyData = _currencyNameTable.GetCurrencyData(pp.Currency, true);
            return new PullPaymentsModel.PullPaymentModel.ProgressModel()
            {
                CompletedPercent = (int)(totalCompleted / pp.Limit * 100m),
                AwaitingPercent = (int)(totalAwaiting / pp.Limit * 100m),
                AwaitingFormatted = totalAwaiting.ToString("C", nfi),
                Awaiting = totalAwaiting,
                Completed = totalCompleted,
                CompletedFormatted = totalCompleted.ToString("C", nfi),
                Limit = pp.Limit.RoundToSignificant(currencyData.Divisibility),
                LimitFormatted = _displayFormatter.Currency(pp.Limit, pp.Currency),
                EndIn = pp.EndsIn() is { } end ? end.TimeString() : null,
            };
        }


        public TimeSpan ZeroIfNegative(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
                time = TimeSpan.Zero;
            return time;
        }

        public static string GetInternalTag(string ppId)
        {
            return $"PULLPAY#{ppId}";
        }

        class InternalPayoutPaidRequest
        {
            public InternalPayoutPaidRequest(TaskCompletionSource<MarkPayoutRequest.PayoutPaidResult> completionSource,
                MarkPayoutRequest request)
            {
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(completionSource);
                Completion = completionSource;
                Request = request;
            }

            public TaskCompletionSource<MarkPayoutRequest.PayoutPaidResult> Completion { get; set; }
            public MarkPayoutRequest Request { get; }
        }
    }

    public class MarkPayoutRequest
    {
        public enum PayoutPaidResult
        {
            Ok,
            NotFound,
            InvalidState
        }

        public string PayoutId { get; set; }
        public JObject Proof { get; set; }
        public PayoutState State { get; set; } = PayoutState.Completed;

        public static string GetErrorMessage(PayoutPaidResult result)
        {
            switch (result)
            {
                case PayoutPaidResult.NotFound:
                    return "The payout is not found";
                case PayoutPaidResult.Ok:
                    return "Ok";
                case PayoutPaidResult.InvalidState:
                    return "The payout is not in a state that can be marked with the specified state";
                default:
                    throw new NotSupportedException();
            }
        }
    }

    public class ClaimRequest
    {
        public static (string error, decimal? amount) IsPayoutAmountOk(IClaimDestination destination, decimal? amount, string payoutCurrency = null, string ppCurrency = null)
        {
            return amount switch
            {
                null when destination.Amount is null && ppCurrency is null => ("Amount is not specified in destination or payout request", null),
                null when destination.Amount is null => (null, null),
                null when destination.Amount != null => (null, destination.Amount),
                not null when destination.Amount is null => (null, amount),
                not null when destination.Amount != null && amount != destination.Amount &&
                              destination.IsExplicitAmountMinimum &&
                              payoutCurrency == "BTC" && ppCurrency == "SATS" &&
                              new Money(amount.Value, MoneyUnit.Satoshi).ToUnit(MoneyUnit.BTC) < destination.Amount =>
                    ($"Amount is implied in both destination ({destination.Amount}) and payout request ({amount}), but the payout request amount is less than the destination amount", null),
                not null when destination.Amount != null && amount != destination.Amount &&
                              destination.IsExplicitAmountMinimum &&
                              !(payoutCurrency == "BTC" && ppCurrency == "SATS") &&
                              amount < destination.Amount =>
                    ($"Amount is implied in both destination ({destination.Amount}) and payout request ({amount}), but the payout request amount is less than the destination amount", null),
                not null when destination.Amount != null && amount != destination.Amount &&
                              !destination.IsExplicitAmountMinimum =>
                    ($"Amount is implied in destination ({destination.Amount}) that does not match the payout amount provided {amount})", null),
                _ => (null, amount)
            };
        }

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

        public PayoutMethodId PayoutMethodId { get; set; }
        public string PullPaymentId { get; set; }
        public decimal? Value { get; set; }
        public IClaimDestination Destination { get; set; }
        public string StoreId { get; set; }
        public bool? PreApprove { get; set; }
        public JObject Metadata { get; set; }
    }

    public record PayoutEvent(PayoutEvent.PayoutEventType Type, PayoutData Payout)
    {
        public enum PayoutEventType
        {
            Created,
            Approved,
            Updated
        }
    }
}
