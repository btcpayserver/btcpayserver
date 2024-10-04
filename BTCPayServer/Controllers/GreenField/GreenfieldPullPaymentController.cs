#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.NTag424;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MarkPayoutRequest = BTCPayServer.HostedServices.MarkPayoutRequest;

namespace BTCPayServer.Controllers.Greenfield
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
        private readonly PayoutMethodHandlerDictionary _payoutHandlers;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly IAuthorizationService _authorizationService;
        private readonly SettingsRepository _settingsRepository;
        private readonly BTCPayServerEnvironment _env;

        public GreenfieldPullPaymentController(PullPaymentHostedService pullPaymentService,
            LinkGenerator linkGenerator,
            ApplicationDbContextFactory dbContextFactory,
            CurrencyNameTable currencyNameTable,
            Services.BTCPayNetworkJsonSerializerSettings serializerSettings,
            PayoutMethodHandlerDictionary payoutHandlers,
            BTCPayNetworkProvider btcPayNetworkProvider,
            IAuthorizationService authorizationService,
            SettingsRepository settingsRepository,
            BTCPayServerEnvironment env)
        {
            _pullPaymentService = pullPaymentService;
            _linkGenerator = linkGenerator;
            _dbContextFactory = dbContextFactory;
            _currencyNameTable = currencyNameTable;
            _serializerSettings = serializerSettings;
            _payoutHandlers = payoutHandlers;
            _networkProvider = btcPayNetworkProvider;
            _authorizationService = authorizationService;
            _settingsRepository = settingsRepository;
            _env = env;
        }

        [HttpGet("~/api/v1/stores/{storeId}/pull-payments")]
        [Authorize(Policy = Policies.CanViewPullPayments, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
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
        [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreatePullPayment(string storeId, CreatePullPaymentRequest request)
        {
            if (request is null)
            {
                ModelState.AddModelError(string.Empty, "Missing body");
                return this.CreateValidationError(ModelState);
            }

            if (request.AutoApproveClaims)
            {
                if (!(await _authorizationService.AuthorizeAsync(User, null,
                        new PolicyRequirement(Policies.CanCreatePullPayments))).Succeeded)
                {
                    return this.CreateAPIPermissionError(Policies.CanCreatePullPayments);
                }

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
            if (request.BOLT11Expiration < TimeSpan.Zero)
            {
                ModelState.AddModelError(nameof(request.BOLT11Expiration), $"The BOLT11 expiration should be positive");
            }
            PayoutMethodId?[]? payoutMethods = null;
            if (request.PayoutMethods is { } payoutMethodsStr)
            {
                payoutMethods = payoutMethodsStr.Select(s =>
                {
                    PayoutMethodId.TryParse(s, out var pmi);
                    return pmi;
                }).ToArray();
                var supported =  _payoutHandlers.GetSupportedPayoutMethods(HttpContext.GetStoreData());
                for (int i = 0; i < payoutMethods.Length; i++)
                {
                    if (!supported.Contains(payoutMethods[i]))
                    {
                        request.AddModelError(paymentRequest => paymentRequest.PayoutMethods[i], "Invalid or unsupported payment method", this);
                    }
                }
            }
            else
            {
                ModelState.AddModelError(nameof(request.PayoutMethods), "This field is required");
            }
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            var ppId = await _pullPaymentService.CreatePullPayment(storeId, request);
            var pp = await _pullPaymentService.GetPullPayment(ppId, false);
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
                Amount = pp.Limit,
                Name = ppBlob.Name,
                Description = ppBlob.Description,
                Currency = pp.Currency,
                Archived = pp.Archived,
                AutoApproveClaims = ppBlob.AutoApproveClaims,
                BOLT11Expiration = ppBlob.BOLT11Expiration,
                ViewLink = _linkGenerator.GetUriByAction(
                                nameof(UIPullPaymentController.ViewPullPayment),
                                "UIPullPayment",
                                new { pullPaymentId = pp.Id },
                                Request.Scheme,
                                Request.Host,
                                Request.PathBase)
            };
        }

        [HttpPost]
        [Route("~/api/v1/pull-payments/{pullPaymentId}/boltcards")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterBoltcard(string pullPaymentId, RegisterBoltcardRequest request, string? onExisting = null)
        {
            if (pullPaymentId is null)
                return PullPaymentNotFound();
            var pp = await _pullPaymentService.GetPullPayment(pullPaymentId, false);
            if (pp is null)
                return PullPaymentNotFound();
            var issuerKey = await _settingsRepository.GetIssuerKey(_env);

            // LNURLW is used by deeplinks
            if (request?.LNURLW is not null)
            {
                if (request.UID is not null)
                {
                    ModelState.AddModelError(nameof(request.LNURLW), "You should pass either LNURLW or UID but not both");
                    return this.CreateValidationError(ModelState);
                }
                var p = ExtractP(request.LNURLW);
                if (p is null)
                {
                    ModelState.AddModelError(nameof(request.LNURLW), "The LNURLW should contains a 'p=' parameter");
                    return this.CreateValidationError(ModelState);
                }
                if (issuerKey.TryDecrypt(p) is not BoltcardPICCData picc)
                {
                    ModelState.AddModelError(nameof(request.LNURLW), "The LNURLW 'p=' parameter cannot be decrypted");
                    return this.CreateValidationError(ModelState);
                }
                request.UID = picc.Uid;
            }

            if (request?.UID is null || request.UID.Length != 7)
            {
                ModelState.AddModelError(nameof(request.UID), "The UID is required and should be 7 bytes");
                return this.CreateValidationError(ModelState);
            }
            if (!_pullPaymentService.SupportsLNURL(pp))
            {
                return this.CreateAPIError(400, "lnurl-not-supported", "This pull payment currency should be BTC or SATS and accept lightning");
            }

            // Passing onExisting as a query parameter is used by deeplink
            request.OnExisting = onExisting switch
            {
                nameof(OnExistingBehavior.UpdateVersion) => OnExistingBehavior.UpdateVersion,
                nameof(OnExistingBehavior.KeepVersion) => OnExistingBehavior.KeepVersion,
                _ => request.OnExisting
            };

            var version = await _dbContextFactory.LinkBoltcardToPullPayment(pullPaymentId, issuerKey, request.UID, request.OnExisting);
            var keys = issuerKey.CreatePullPaymentCardKey(request.UID, version, pullPaymentId).DeriveBoltcardKeys(issuerKey);

            var boltcardUrl = Url.Action(nameof(UIBoltcardController.GetWithdrawRequest), "UIBoltcard");
            boltcardUrl = Request.GetAbsoluteUri(boltcardUrl);
            boltcardUrl = Regex.Replace(boltcardUrl, "^https?://", "lnurlw://");

            var resp = new RegisterBoltcardResponse()
            {
                LNURLW = boltcardUrl,
                Version = version,
                K0 = Encoders.Hex.EncodeData(keys.AppMasterKey.ToBytes()).ToUpperInvariant(),
                K1 = Encoders.Hex.EncodeData(keys.EncryptionKey.ToBytes()).ToUpperInvariant(),
                K2 = Encoders.Hex.EncodeData(keys.AuthenticationKey.ToBytes()).ToUpperInvariant(),
                K3 = Encoders.Hex.EncodeData(keys.K3.ToBytes()).ToUpperInvariant(),
                K4 = Encoders.Hex.EncodeData(keys.K4.ToBytes()).ToUpperInvariant(),
            };
            return Ok(resp);
        }

        private string? ExtractP(string? url)
        {
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;
            int num = uri.AbsoluteUri.IndexOf('?');
            if (num == -1)
                return null;
            string input = uri.AbsoluteUri.Substring(num);
            Match match = Regex.Match(input, "p=([a-f0-9A-F]{32})");
            if (!match.Success)
                return null;
            return match.Groups[1].Value;
        }

        [HttpGet("~/api/v1/pull-payments/{pullPaymentId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPullPayment(string pullPaymentId)
        {
            if (pullPaymentId is null)
                return PullPaymentNotFound();
            var pp = await _pullPaymentService.GetPullPayment(pullPaymentId, false);
            if (pp is null)
                return PullPaymentNotFound();
            return Ok(CreatePullPaymentData(pp));
        }

        private PayoutState[]? GetStateFilter(bool includeCancelled) =>
            includeCancelled
                ? null
                : new[]
                {
                    PayoutState.Completed, PayoutState.AwaitingApproval, PayoutState.AwaitingPayment,
                    PayoutState.InProgress
                };

        [HttpGet("~/api/v1/pull-payments/{pullPaymentId}/payouts")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPayouts(string pullPaymentId, bool includeCancelled = false)
        {
            if (pullPaymentId is null)
                return PullPaymentNotFound();
            var pp = await _pullPaymentService.GetPullPayment(pullPaymentId, true);
            if (pp is null)
                return PullPaymentNotFound();

            var payouts = await _pullPaymentService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
            {
                PullPayments = new[] { pullPaymentId },
                States = GetStateFilter(includeCancelled)
            });
            return base.Ok(payouts
                    .Select(ToModel).ToList());
        }

        [HttpGet("~/api/v1/pull-payments/{pullPaymentId}/payouts/{payoutId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPayout(string pullPaymentId, string payoutId)
        {
            if (payoutId is null)
                return PayoutNotFound();
            await using var ctx = _dbContextFactory.CreateContext();

            var payout = (await _pullPaymentService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
            {
                PullPayments = new[] { pullPaymentId },
                PayoutIds = new[] { payoutId }
            })).FirstOrDefault();


            if (payout is null)
                return PayoutNotFound();
            return base.Ok(ToModel(payout));
        }

        [HttpGet("~/api/v1/pull-payments/{pullPaymentId}/lnurl")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPullPaymentLNURL(string pullPaymentId)
        {
            var pp = await _pullPaymentService.GetPullPayment(pullPaymentId, false);
            if (pp is null)
                return PullPaymentNotFound();

            if (_pullPaymentService.SupportsLNURL(pp))
            {
                var lnurlEndpoint = new Uri(Url.Action("GetLNURLForPullPayment", "UILNURL", new
                {
                    cryptoCode = _networkProvider.DefaultNetwork.CryptoCode,
                    pullPaymentId
                }, Request.Scheme, Request.Host.ToString())!);

                return base.Ok(new PullPaymentLNURL
                {
                    LNURLBech32 = LNURL.LNURL.EncodeUri(lnurlEndpoint, "withdrawRequest", true).ToString(),
                    LNURLUri = LNURL.LNURL.EncodeUri(lnurlEndpoint, "withdrawRequest", false).ToString()
                });
            }

            return this.CreateAPIError("lnurl-not-supported", "LNURL not supported for this pull payment");
        }

        private Client.Models.PayoutData ToModel(Data.PayoutData p)
        {
            var blob = p.GetBlob(_serializerSettings);
            var model = new Client.Models.PayoutData()
            {
                Id = p.Id,
                PullPaymentId = p.PullPaymentDataId,
                Date = p.Date,
                OriginalCurrency = p.OriginalCurrency,
                OriginalAmount = p.OriginalAmount,
                PayoutCurrency = p.Currency,
                PayoutAmount = p.Amount,
                Revision = blob.Revision,
                State = p.State,
                PayoutMethodId = p.PayoutMethodId,
                PaymentProof = p.GetProofBlobJson(),
                Destination = blob.Destination,
                Metadata = blob.Metadata?? new JObject(),
            };
            return model;
        }

        [HttpPost("~/api/v1/pull-payments/{pullPaymentId}/payouts")]
        [AllowAnonymous]
        public async Task<IActionResult> CreatePayout(string pullPaymentId, CreatePayoutRequest request, CancellationToken cancellationToken)
        {
            if (!PayoutMethodId.TryParse(request?.PayoutMethodId, out var payoutMethodId))
            {
                ModelState.AddModelError(nameof(request.PayoutMethodId), "Invalid payment method");
                return this.CreateValidationError(ModelState);
            }

            var payoutHandler = _payoutHandlers.TryGet(payoutMethodId);
            if (payoutHandler is null)
            {
                ModelState.AddModelError(nameof(request.PayoutMethodId), "Invalid payment method");
                return this.CreateValidationError(ModelState);
            }

            await using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null)
                return PullPaymentNotFound();
            var ppBlob = pp.GetBlob();
            var destination = await payoutHandler.ParseAndValidateClaimDestination(request!.Destination, ppBlob, cancellationToken);
            if (destination.destination is null)
            {
                ModelState.AddModelError(nameof(request.Destination), destination.error ?? "The destination is invalid for the payment specified");
                return this.CreateValidationError(ModelState);
            }
            
            var amtError = ClaimRequest.IsPayoutAmountOk(destination.destination, request.Amount, payoutHandler.Currency, pp.Currency);
            if (amtError.error is not null)
            {
                ModelState.AddModelError(nameof(request.Amount), amtError.error );
                return this.CreateValidationError(ModelState);
            }
            request.Amount = amtError.amount;
            var result = await _pullPaymentService.Claim(new ClaimRequest()
            {
                Destination = destination.destination,
                PullPaymentId = pullPaymentId,
                Value = request.Amount,
                PayoutMethodId = payoutMethodId,
                StoreId = pp.StoreId
            });

            return HandleClaimResult(result);
        }

        [HttpPost("~/api/v1/stores/{storeId}/payouts")]
        [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreatePayoutThroughStore(string storeId, CreatePayoutThroughStoreRequest request)
        {
            if (request?.Approved is true)
            {
                if (!(await _authorizationService.AuthorizeAsync(User, null,
                        new PolicyRequirement(Policies.CanCreatePullPayments))).Succeeded)
                {
                    return this.CreateAPIPermissionError(Policies.CanCreatePullPayments);
                }
            }

            if (request?.PayoutMethodId is null || !PayoutMethodId.TryParse(request?.PayoutMethodId, out var paymentMethodId))
            {
                ModelState.AddModelError(nameof(request.PayoutMethodId), "Invalid payment method");
                return this.CreateValidationError(ModelState);
            }

            var payoutHandler = _payoutHandlers.TryGet(paymentMethodId);
            if (payoutHandler is null)
            {
                ModelState.AddModelError(nameof(request.PayoutMethodId), "Invalid payment method");
                return this.CreateValidationError(ModelState);
            }

            await using var ctx = _dbContextFactory.CreateContext();


            PullPaymentBlob? ppBlob = null;
            if (request?.PullPaymentId is not null)
            {

                var pp = await ctx.PullPayments.FirstOrDefaultAsync(data =>
                    data.Id == request.PullPaymentId && data.StoreId == storeId);
                if (pp is null)
                    return PullPaymentNotFound();
                ppBlob = pp.GetBlob();
            }
            var destination = await payoutHandler.ParseAndValidateClaimDestination(request!.Destination, ppBlob, default);
            if (destination.destination is null)
            {
                ModelState.AddModelError(nameof(request.Destination), destination.error ?? "The destination is invalid for the payment specified");
                return this.CreateValidationError(ModelState);
            }

            var amtError = ClaimRequest.IsPayoutAmountOk(destination.destination, request.Amount);
            if (amtError.error is not null)
            {
                ModelState.AddModelError(nameof(request.Amount), amtError.error );
                return this.CreateValidationError(ModelState);
            }
            request.Amount = amtError.amount;
            if (request.Amount is { } v && (v < ppBlob?.MinimumClaim || v == 0.0m))
            {
                var minimumClaim = ppBlob?.MinimumClaim is decimal val ? val : 0.0m;
                ModelState.AddModelError(nameof(request.Amount), $"Amount too small (should be at least {minimumClaim})");
                return this.CreateValidationError(ModelState);
            }
            var result = await _pullPaymentService.Claim(new ClaimRequest()
            {
                Destination = destination.destination,
                PullPaymentId = request.PullPaymentId,
                PreApprove = request.Approved,
                Value = request.Amount,
                PayoutMethodId = paymentMethodId,
                StoreId = storeId,
                Metadata = request.Metadata
            });
            return HandleClaimResult(result);
        }

        private IActionResult HandleClaimResult(ClaimRequest.ClaimResponse result)
        {
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

            return Ok(ToModel(result.PayoutData));
        }

        [HttpDelete("~/api/v1/stores/{storeId}/pull-payments/{pullPaymentId}")]
        [Authorize(Policy = Policies.CanArchivePullPayments, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> ArchivePullPayment(string storeId, string pullPaymentId)
        {
            using var ctx = _dbContextFactory.CreateContext();
            var pp = await ctx.PullPayments.FindAsync(pullPaymentId);
            if (pp is null || pp.StoreId != storeId)
                return PullPaymentNotFound();
            await _pullPaymentService.Cancel(new PullPaymentHostedService.CancelRequest(pullPaymentId));
            return Ok();
        }



        [HttpGet("~/api/v1/stores/{storeId}/payouts")]
        [Authorize(Policy = Policies.CanViewPayouts, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetStorePayouts(string storeId, bool includeCancelled = false)
        {
            var payouts = await _pullPaymentService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
            {
                Stores = new[] { storeId },
                States = GetStateFilter(includeCancelled)
            });


            return base.Ok(payouts
                .Select(ToModel).ToArray());
        }

        [HttpDelete("~/api/v1/stores/{storeId}/payouts/{payoutId}")]
        [Authorize(Policy = Policies.CanManagePayouts, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CancelPayout(string storeId, string payoutId)
        {
            var res = await _pullPaymentService.Cancel(new PullPaymentHostedService.CancelRequest(new[] { payoutId }, new[] { storeId }));
            return MapResult(res.First().Value);
        }

        [HttpPost("~/api/v1/stores/{storeId}/payouts/{payoutId}")]
        [Authorize(Policy = Policies.CanManagePayouts, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
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
                return PayoutNotFound();
            RateResult? rateResult = null;
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
            var result = (await _pullPaymentService.Approve(new PullPaymentHostedService.PayoutApproval()
            {
                PayoutId = payoutId,
                Revision = revision!.Value,
                Rate = rateResult.BidAsk.Ask
            })).Result;
            var errorMessage = PullPaymentHostedService.PayoutApproval.GetErrorMessage(result);
            switch (result)
            {
                case PullPaymentHostedService.PayoutApproval.Result.Ok:
                    return Ok(ToModel(await ctx.Payouts.GetPayout(payoutId, storeId, true)));
                case PullPaymentHostedService.PayoutApproval.Result.InvalidState:
                    return this.CreateAPIError("invalid-state", errorMessage);
                case PullPaymentHostedService.PayoutApproval.Result.TooLowAmount:
                    return this.CreateAPIError("amount-too-low", errorMessage);
                case PullPaymentHostedService.PayoutApproval.Result.OldRevision:
                    return this.CreateAPIError("old-revision", errorMessage);
                case PullPaymentHostedService.PayoutApproval.Result.NotFound:
                    return PayoutNotFound();
                default:
                    throw new NotSupportedException();
            }
        }

        [HttpPost("~/api/v1/stores/{storeId}/payouts/{payoutId}/mark-paid")]
        [Authorize(Policy = Policies.CanManagePayouts, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> MarkPayoutPaid(string storeId, string payoutId, CancellationToken cancellationToken = default)
        {
            return await MarkPayout(storeId, payoutId, new Client.Models.MarkPayoutRequest()
            {
                State = PayoutState.Completed,
                PaymentProof = null
            });
        }

        [HttpPost("~/api/v1/stores/{storeId}/payouts/{payoutId}/mark")]
        [Authorize(Policy = Policies.CanManagePayouts, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> MarkPayout(string storeId, string payoutId, Client.Models.MarkPayoutRequest request)
        {
            request ??= new();

            if (request.State == PayoutState.Cancelled)
            {
                return await CancelPayout(storeId, payoutId);
            }
            if (request.PaymentProof is not null &&
                !BitcoinLikePayoutHandler.TryParseProofType(request.PaymentProof, out string _))
            {
                ModelState.AddModelError(nameof(request.PaymentProof), "Payment proof must have a 'proofType' property");
            }
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            var result = await _pullPaymentService.MarkPaid(new MarkPayoutRequest()
            {
                Proof = request.PaymentProof,
                PayoutId = payoutId,
                State = request.State
            });
            return MapResult(result);
        }

        [HttpGet("~/api/v1/stores/{storeId}/payouts/{payoutId}")]
        [Authorize(Policy = Policies.CanViewPayouts, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetStorePayout(string storeId, string payoutId)
        {
            await using var ctx = _dbContextFactory.CreateContext();

            var payout = (await _pullPaymentService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
            {
                Stores = new[] { storeId },
                PayoutIds = new[] { payoutId }
            })).FirstOrDefault();

            if (payout is null)
                return PayoutNotFound();
            return base.Ok(ToModel(payout));
        }

        private IActionResult MapResult(MarkPayoutRequest.PayoutPaidResult result)
        {
            var errorMessage = MarkPayoutRequest.GetErrorMessage(result);
            return result switch
            {
                MarkPayoutRequest.PayoutPaidResult.Ok => Ok(),
                MarkPayoutRequest.PayoutPaidResult.InvalidState => this.CreateAPIError("invalid-state", errorMessage),
                MarkPayoutRequest.PayoutPaidResult.NotFound => PayoutNotFound(),
                _ => throw new NotSupportedException()
            };
        }
        private IActionResult PayoutNotFound()
        {
            return this.CreateAPIError(404, "payout-not-found", "The payout was not found");
        }
        private IActionResult PullPaymentNotFound()
        {
            return this.CreateAPIError(404, "pullpayment-not-found", "The pull payment was not found");
        }
    }
}
