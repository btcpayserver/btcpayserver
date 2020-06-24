using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.ModelBinders;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using ExchangeSharp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NBitpayClient;
using NUglify.Helpers;
using Org.BouncyCastle.Ocsp;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class GreenfieldPullPaymentController : ControllerBase
    {
        private readonly PullPaymentHostedService _pullPaymentService;
        private readonly LinkGenerator _linkGenerator;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly BTCPayNetworkJsonSerializerSettings _serializerSettings;
        private readonly BTCPayNetworkProvider _networkProvider;

        public GreenfieldPullPaymentController(PullPaymentHostedService pullPaymentService,
            LinkGenerator linkGenerator,
            ApplicationDbContextFactory dbContextFactory,
            CurrencyNameTable currencyNameTable,
            Services.BTCPayNetworkJsonSerializerSettings serializerSettings,
            BTCPayNetworkProvider networkProvider)
        {
            _pullPaymentService = pullPaymentService;
            _linkGenerator = linkGenerator;
            _dbContextFactory = dbContextFactory;
            _currencyNameTable = currencyNameTable;
            _serializerSettings = serializerSettings;
            _networkProvider = networkProvider;
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
            BTCPayNetwork network = null;
            if (request.Currency is String currency)
            {
                network = _networkProvider.GetNetwork<BTCPayNetwork>(currency);
                if (network is null)
                {
                    ModelState.AddModelError(nameof(request.Currency), $"Only crypto currencies are supported this field. (More will be supported soon)");
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
            if (request.PaymentMethods is string[] paymentMethods)
            {
                if (paymentMethods.Length != 1 && paymentMethods[0] != request.Currency)
                {
                    ModelState.AddModelError(nameof(request.PaymentMethods), "We expect this array to only contains the same element as the `currency` field. (More will be supported soon)");
                }
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
                Currency = network.CryptoCode,
                StoreId = storeId,
                PaymentMethodIds = new[] { new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike) }
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
                        .Where(p => p.State != Data.PayoutState.Cancelled || includeCancelled)
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
                State = p.State == Data.PayoutState.AwaitingPayment ? Client.Models.PayoutState.AwaitingPayment :
                                            p.State == Data.PayoutState.Cancelled ? Client.Models.PayoutState.Cancelled :
                                            p.State == Data.PayoutState.Completed ? Client.Models.PayoutState.Completed :
                                            p.State == Data.PayoutState.InProgress ? Client.Models.PayoutState.InProgress :
                                            throw new NotSupportedException(),
            };
            model.Destination = blob.Destination.ToString();
            model.PaymentMethod = p.PaymentMethodId;
            return model;
        }

        [HttpPost("~/api/v1/pull-payments/{pullPaymentId}/payouts")]
        [AllowAnonymous]
        public async Task<IActionResult> CreatePayout(string pullPaymentId, CreatePayoutRequest request)
        {
            if (request is null)
                return NotFound();

            var network = request?.PaymentMethod is string paymentMethod ? 
                            this._networkProvider.GetNetwork<BTCPayNetwork>(paymentMethod) : null;
            if (network is null)
            {
                ModelState.AddModelError(nameof(request.PaymentMethod), "Invalid payment method");
                return this.CreateValidationError(ModelState);
            }

            using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null)
                return NotFound();
            var ppBlob = pp.GetBlob();
            if (request.Destination is null || !ClaimDestination.TryParse(request.Destination, network, out var destination))
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
                PaymentMethodId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike)
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
    }
}
