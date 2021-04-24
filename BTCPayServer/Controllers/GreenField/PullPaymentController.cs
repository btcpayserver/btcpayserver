using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldPullPaymentController : ControllerBase
    {
        private readonly PullPaymentHostedService _pullPaymentService;
        private readonly LinkGenerator _linkGenerator;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly BTCPayNetworkJsonSerializerSettings _serializerSettings;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly IEnumerable<IPayoutHandler> _payoutHandlers;

        public GreenfieldPullPaymentController(PullPaymentHostedService pullPaymentService,
            LinkGenerator linkGenerator,
            ApplicationDbContextFactory dbContextFactory,
            CurrencyNameTable currencyNameTable,
            Services.BTCPayNetworkJsonSerializerSettings serializerSettings,
            BTCPayNetworkProvider networkProvider,
            IEnumerable<IPayoutHandler> payoutHandlers)
        {
            _pullPaymentService = pullPaymentService;
            _linkGenerator = linkGenerator;
            _dbContextFactory = dbContextFactory;
            _currencyNameTable = currencyNameTable;
            _serializerSettings = serializerSettings;
            _networkProvider = networkProvider;
            _payoutHandlers = payoutHandlers;
        }

        [HttpGet("~/api/v1/stores/{storeId}/pull-payments")]
        [Authorize(Policy = Policies.CanManagePullPayments, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetPullPayments(string storeId, bool includeArchived = false)
        {
            using var ctx = _dbContextFactory.CreateContext();
            var pps = await ctx.PullPayments
                .Where(p => p.StoreId == storeId && (includeArchived || !p.Archived))
                .OrderByDescending(p => p.StartDate)
                .ToListAsync();
            return Ok(pps.Select(CreatePullPaymentData).ToArray());
        }

        [HttpPost("~/api/v1/stores/{storeId}/pull-payments")]
        [Authorize(Policy = Policies.CanManagePullPayments, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreatePullPayment(string storeId, CreatePullPaymentRequest request)
        {
            if (request is null)
            {
                ModelState.AddModelError(string.Empty, "Missing body");
                return this.CreateValidationError(ModelState);
            }
            if (request.Amount <= 0.0m)
            {
                ModelState.AddModelError(nameof(request.Amount), "The amount should more than 0.");
            }
            if (request.Name is String name && name.Length > 50)
            {
                ModelState.AddModelError(nameof(request.Name), "The name should be maximum 50 characters.");
            }
            if (request.Currency is String currency)
            {
                request.Currency = currency.ToUpperInvariant().Trim();
                if (_currencyNameTable.GetCurrencyData(request.Currency, false) is null)
                {
                    ModelState.AddModelError(nameof(request.Currency), "Invalid currency");
                }
            }
            else
            {
                ModelState.AddModelError(nameof(request.Currency), "This field is required");
            }
            if (request.ExpiresAt is DateTimeOffset expires && request.StartsAt is DateTimeOffset start && expires < start)
            {
                ModelState.AddModelError(nameof(request.ExpiresAt), $"expiresAt should be higher than startAt");
            }
            if (request.Period <= TimeSpan.Zero)
            {
                ModelState.AddModelError(nameof(request.Period), $"The period should be positive");
            }
            PaymentMethodId[] paymentMethods = null;
            if (request.PaymentMethods is string[] paymentMethodsStr)
            {
                paymentMethods = paymentMethodsStr.Select(p => new PaymentMethodId(p, PaymentTypes.BTCLike)).ToArray();
                foreach (var p in paymentMethods)
                {
                    var n = _networkProvider.GetNetwork<BTCPayNetwork>(p.CryptoCode);
                    if (n is null)
                        ModelState.AddModelError(nameof(request.PaymentMethods), "Invalid payment method");
                    if (n.ReadonlyWallet)
                        ModelState.AddModelError(nameof(request.PaymentMethods), "Invalid payment method (We do not support the crypto currency for refund)");
                }
                if (paymentMethods.Any(p => _networkProvider.GetNetwork<BTCPayNetwork>(p.CryptoCode) is null))
                    ModelState.AddModelError(nameof(request.PaymentMethods), "Invalid payment method");
            }
            else
            {
                ModelState.AddModelError(nameof(request.PaymentMethods), "This field is required");
            }
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            var ppId = await _pullPaymentService.CreatePullPayment(new HostedServices.CreatePullPayment()
            {
                StartsAt = request.StartsAt,
                ExpiresAt = request.ExpiresAt,
                Period = request.Period,
                Name = request.Name,
                Amount = request.Amount,
                Currency = request.Currency,
                StoreId = storeId,
                PaymentMethodIds = paymentMethods
            });
            var pp = await _pullPaymentService.GetPullPayment(ppId);
            return this.Ok(CreatePullPaymentData(pp));
        }

        private Client.Models.PullPaymentData CreatePullPaymentData(Data.PullPaymentData pp)
        {
            var ppBlob = pp.GetBlob();
            return new BTCPayServer.Client.Models.PullPaymentData()
            {
                Id = pp.Id,
                StartsAt = pp.StartDate,
                ExpiresAt = pp.EndDate,
                Amount = ppBlob.Limit,
                Name = ppBlob.Name,
                Currency = ppBlob.Currency,
                Period = ppBlob.Period,
                Archived = pp.Archived,
                ViewLink = _linkGenerator.GetUriByAction(
                                nameof(PullPaymentController.ViewPullPayment),
                                "PullPayment",
                                new { pullPaymentId = pp.Id },
                                Request.Scheme,
                                Request.Host,
                                Request.PathBase)
            };
        }

        [HttpGet("~/api/v1/pull-payments/{pullPaymentId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPullPayment(string pullPaymentId)
        {
            if (pullPaymentId is null)
                return NotFound();
            var pp = await _pullPaymentService.GetPullPayment(pullPaymentId);
            if (pp is null)
                return NotFound();
            return Ok(CreatePullPaymentData(pp));
        }

        [HttpGet("~/api/v1/pull-payments/{pullPaymentId}/payouts")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPayouts(string pullPaymentId, bool includeCancelled = false)
        {
            if (pullPaymentId is null)
                return NotFound();
            var pp = await _pullPaymentService.GetPullPayment(pullPaymentId);
            if (pp is null)
                return NotFound();
            using var ctx = _dbContextFactory.CreateContext();
            var payouts = await ctx.Payouts.Where(p => p.PullPaymentDataId == pullPaymentId)
                        .Where(p => p.State != PayoutState.Cancelled || includeCancelled)
                       .ToListAsync();
            var cd = _currencyNameTable.GetCurrencyData(pp.GetBlob().Currency, false);
            return base.Ok(payouts
                    .Select(p => ToModel(p, cd)).ToList());
        }

        private Client.Models.PayoutData ToModel(Data.PayoutData p, CurrencyData cd)
        {
            var blob = p.GetBlob(_serializerSettings);
            var model = new Client.Models.PayoutData()
            {
                Id = p.Id,
                PullPaymentId = p.PullPaymentDataId,
                Date = p.Date,
                Amount = blob.Amount,
                PaymentMethodAmount = blob.CryptoAmount,
                Revision = blob.Revision,
                State = p.State
            };
            model.Destination = blob.Destination;
            model.PaymentMethod = p.PaymentMethodId;
            return model;
        }

        [HttpPost("~/api/v1/pull-payments/{pullPaymentId}/payouts")]
        [AllowAnonymous]
        public async Task<IActionResult> CreatePayout(string pullPaymentId, CreatePayoutRequest request)
        {
            if (request is null)
                return NotFound();
            if (!PaymentMethodId.TryParse(request?.PaymentMethod, out var paymentMethodId))
            {
                ModelState.AddModelError(nameof(request.PaymentMethod), "Invalid payment method");
                return this.CreateValidationError(ModelState);
            }
            
            var payoutHandler = _payoutHandlers.FirstOrDefault(handler => handler.CanHandle(paymentMethodId));
            if (payoutHandler is null)
            {
                ModelState.AddModelError(nameof(request.PaymentMethod), "Invalid payment method");
                return this.CreateValidationError(ModelState);
            }

            using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null)
                return NotFound();
            var ppBlob = pp.GetBlob();
            IClaimDestination destination = await payoutHandler.ParseClaimDestination(paymentMethodId,request.Destination);
            if (destination is null)
            {
                ModelState.AddModelError(nameof(request.Destination), "The destination must be an address or a BIP21 URI");
                return this.CreateValidationError(ModelState);
            }

            if (request.Amount is decimal v && (v < ppBlob.MinimumClaim || v == 0.0m))
            {
                ModelState.AddModelError(nameof(request.Amount), $"Amount too small (should be at least {ppBlob.MinimumClaim})");
                return this.CreateValidationError(ModelState);
            }
            var cd = _currencyNameTable.GetCurrencyData(pp.GetBlob().Currency, false);
            var result = await _pullPaymentService.Claim(new ClaimRequest()
            {
                Destination = destination,
                PullPaymentId = pullPaymentId,
                Value = request.Amount,
                PaymentMethodId = paymentMethodId
            });
            switch (result.Result)
            {
                case ClaimRequest.ClaimResult.Ok:
                    break;
                case ClaimRequest.ClaimResult.Duplicate:
                    return this.CreateAPIError("duplicate-destination", ClaimRequest.GetErrorMessage(result.Result));
                case ClaimRequest.ClaimResult.Expired:
                    return this.CreateAPIError("expired", ClaimRequest.GetErrorMessage(result.Result));
                case ClaimRequest.ClaimResult.NotStarted:
                    return this.CreateAPIError("not-started", ClaimRequest.GetErrorMessage(result.Result));
                case ClaimRequest.ClaimResult.Archived:
                    return this.CreateAPIError("archived", ClaimRequest.GetErrorMessage(result.Result));
                case ClaimRequest.ClaimResult.Overdraft:
                    return this.CreateAPIError("overdraft", ClaimRequest.GetErrorMessage(result.Result));
                case ClaimRequest.ClaimResult.AmountTooLow:
                    return this.CreateAPIError("amount-too-low", ClaimRequest.GetErrorMessage(result.Result));
                case ClaimRequest.ClaimResult.PaymentMethodNotSupported:
                    return this.CreateAPIError("payment-method-not-supported", ClaimRequest.GetErrorMessage(result.Result));
                default:
                    throw new NotSupportedException("Unsupported ClaimResult");
            }
            return Ok(ToModel(result.PayoutData, cd));
        }

        [HttpDelete("~/api/v1/stores/{storeId}/pull-payments/{pullPaymentId}")]
        [Authorize(Policy = Policies.CanManagePullPayments, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ArchivePullPayment(string storeId, string pullPaymentId)
        {
            using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null || pp.StoreId != storeId)
                return NotFound();
            await _pullPaymentService.Cancel(new PullPaymentHostedService.CancelRequest(pullPaymentId));
            return Ok();
        }

        [HttpDelete("~/api/v1/stores/{storeId}/payouts/{payoutId}")]
        [Authorize(Policy = Policies.CanManagePullPayments, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CancelPayout(string storeId, string payoutId)
        {
            using var ctx = _dbContextFactory.CreateContext();
            var payout = await ctx.Payouts.GetPayout(payoutId, storeId);
            if (payout is null)
                return NotFound();
            await _pullPaymentService.Cancel(new PullPaymentHostedService.CancelRequest(new[] { payoutId }));
            return Ok();
        }

        [HttpPost("~/api/v1/stores/{storeId}/payouts/{payoutId}")]
        [Authorize(Policy = Policies.CanManagePullPayments, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ApprovePayout(string storeId, string payoutId, ApprovePayoutRequest approvePayoutRequest, CancellationToken cancellationToken = default)
        {
            using var ctx = _dbContextFactory.CreateContext();
            ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var revision = approvePayoutRequest?.Revision;
            if (revision is null)
            {
                ModelState.AddModelError(nameof(approvePayoutRequest.Revision), "The `revision` property is required");
            }
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            var payout = await ctx.Payouts.GetPayout(payoutId, storeId, true, true);
            if (payout is null)
                return NotFound();
            RateResult rateResult = null;
            try
            {
                rateResult = await _pullPaymentService.GetRate(payout, approvePayoutRequest?.RateRule, cancellationToken);
                if (rateResult.BidAsk == null)
                {
                    return this.CreateAPIError("rate-unavailable", $"Rate unavailable: {rateResult.EvaluatedRule}");
                }
            }
            catch (FormatException)
            {
                ModelState.AddModelError(nameof(approvePayoutRequest.RateRule), "Invalid RateRule");
                return this.CreateValidationError(ModelState);
            }
            var ppBlob = payout.PullPaymentData.GetBlob();
            var cd = _currencyNameTable.GetCurrencyData(ppBlob.Currency, false);
            var result = await _pullPaymentService.Approve(new PullPaymentHostedService.PayoutApproval()
            {
                PayoutId = payoutId,
                Revision = revision.Value,
                Rate = rateResult.BidAsk.Ask
            });
            var errorMessage = PullPaymentHostedService.PayoutApproval.GetErrorMessage(result);
            switch (result)
            {
                case PullPaymentHostedService.PayoutApproval.Result.Ok:
                    return Ok(ToModel(await ctx.Payouts.GetPayout(payoutId, storeId, true), cd));
                case PullPaymentHostedService.PayoutApproval.Result.InvalidState:
                    return this.CreateAPIError("invalid-state", errorMessage);
                case PullPaymentHostedService.PayoutApproval.Result.TooLowAmount:
                    return this.CreateAPIError("amount-too-low", errorMessage);
                case PullPaymentHostedService.PayoutApproval.Result.OldRevision:
                    return this.CreateAPIError("old-revision", errorMessage);
                case PullPaymentHostedService.PayoutApproval.Result.NotFound:
                    return NotFound();
                default:
                    throw new NotSupportedException();
            }
        }
    }
}
