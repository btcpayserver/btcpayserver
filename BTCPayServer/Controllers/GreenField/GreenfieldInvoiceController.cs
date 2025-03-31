#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Rating;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;
using CreateInvoiceRequest = BTCPayServer.Client.Models.CreateInvoiceRequest;
using InvoiceData = BTCPayServer.Client.Models.InvoiceData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldInvoiceController : Controller
    {
        private readonly UIInvoiceController _invoiceController;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly LinkGenerator _linkGenerator;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly PullPaymentHostedService _pullPaymentService;
        private readonly RateFetcher _rateProvider;
        private readonly InvoiceActivator _invoiceActivator;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly IAuthorizationService _authorizationService;
        private readonly Dictionary<PaymentMethodId, IPaymentLinkExtension> _paymentLinkExtensions;
        private readonly PayoutMethodHandlerDictionary _payoutHandlers;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly DefaultRulesCollection _defaultRules;

        public LanguageService LanguageService { get; }

        public GreenfieldInvoiceController(UIInvoiceController invoiceController, InvoiceRepository invoiceRepository,
            LinkGenerator linkGenerator, LanguageService languageService,
            CurrencyNameTable currencyNameTable, RateFetcher rateProvider,
            InvoiceActivator invoiceActivator,
            PullPaymentHostedService pullPaymentService, 
            ApplicationDbContextFactory dbContextFactory, 
            IAuthorizationService authorizationService,
            Dictionary<PaymentMethodId, IPaymentLinkExtension> paymentLinkExtensions,
            PayoutMethodHandlerDictionary payoutHandlers,
            PaymentMethodHandlerDictionary handlers,
            BTCPayNetworkProvider networkProvider,
            DefaultRulesCollection defaultRules)
        {
            _invoiceController = invoiceController;
            _invoiceRepository = invoiceRepository;
            _linkGenerator = linkGenerator;
            _currencyNameTable = currencyNameTable;
            _rateProvider = rateProvider;
            _invoiceActivator = invoiceActivator;
            _pullPaymentService = pullPaymentService;
            _dbContextFactory = dbContextFactory;
            _authorizationService = authorizationService;
            _paymentLinkExtensions = paymentLinkExtensions;
            _payoutHandlers = payoutHandlers;
            _handlers = handlers;
            _networkProvider = networkProvider;
            _defaultRules = defaultRules;
            LanguageService = languageService;
        }

        [Authorize(Policy = Policies.CanViewInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/invoices")]
        public async Task<IActionResult> GetInvoices(string storeId, [FromQuery] string[]? orderId = null, [FromQuery] string[]? status = null,
            [FromQuery]
            [ModelBinder(typeof(ModelBinders.DateTimeOffsetModelBinder))]
            DateTimeOffset? startDate = null,
            [FromQuery]
            [ModelBinder(typeof(ModelBinders.DateTimeOffsetModelBinder))]
            DateTimeOffset? endDate = null,
            [FromQuery] string? textSearch = null,
            [FromQuery] bool includeArchived = false,
            [FromQuery] int? skip = null,
            [FromQuery] int? take = null
            )
        {
            var store = HttpContext.GetStoreData()!;
            if (startDate is DateTimeOffset s &&
                endDate is DateTimeOffset e &&
                s > e)
            {
                this.ModelState.AddModelError(nameof(startDate), "startDate should not be above endDate");
                this.ModelState.AddModelError(nameof(endDate), "endDate should not be below startDate");
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            var invoices =
                await _invoiceRepository.GetInvoices(new InvoiceQuery()
                {
                    Skip = skip,
                    Take = take,
                    StoreId = new[] { store.Id },
                    IncludeArchived = includeArchived,
                    StartDate = startDate,
                    EndDate = endDate,
                    OrderId = orderId,
                    Status = status,
                    TextSearch = textSearch
                });

            return Ok(invoices.Select(ToModel));
        }

        [Authorize(Policy = Policies.CanViewInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/invoices/{invoiceId}")]
        public async Task<IActionResult> GetInvoice(string storeId, string invoiceId)
        {
            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (!BelongsToThisStore(invoice))
                return InvoiceNotFound();

            return Ok(ToModel(invoice));
        }

        [Authorize(Policy = Policies.CanModifyInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/invoices/{invoiceId}")]
        public async Task<IActionResult> ArchiveInvoice(string storeId, string invoiceId)
        {
            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (!BelongsToThisStore(invoice))
                return InvoiceNotFound();
            await _invoiceRepository.ToggleInvoiceArchival(invoiceId, true, storeId);
            return Ok();
        }

        [Authorize(Policy = Policies.CanModifyInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/invoices/{invoiceId}")]
        public async Task<IActionResult> UpdateInvoice(string storeId, string invoiceId, UpdateInvoiceRequest request)
        {
            var result = await _invoiceRepository.UpdateInvoiceMetadata(invoiceId, storeId, request.Metadata);
            if (!BelongsToThisStore(result))
                return InvoiceNotFound();
            return Ok(ToModel(result));
        }

        [Authorize(Policy = Policies.CanCreateInvoice,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices")]
        public async Task<IActionResult> CreateInvoice(string storeId, CreateInvoiceRequest request)
        {
            var store = HttpContext.GetStoreData()!;
            if (request.Amount < 0.0m)
            {
                ModelState.AddModelError(nameof(request.Amount), "The amount should be 0 or more.");
            }
            if (request.Amount > GreenfieldConstants.MaxAmount)
            {
                ModelState.AddModelError(nameof(request.Amount), $"The amount should less than {GreenfieldConstants.MaxAmount}.");
            }
            request.Checkout ??= new CreateInvoiceRequest.CheckoutOptions();
            if (request.Checkout.PaymentMethods?.Any() is true)
            {
                for (int i = 0; i < request.Checkout.PaymentMethods.Length; i++)
                {
                    if (
                        request.Checkout.PaymentMethods[i] is not { } pm ||
                        !PaymentMethodId.TryParse(pm, out var pm1) ||
                        _handlers.TryGet(pm1) is null)
                    {
                        request.AddModelError(invoiceRequest => invoiceRequest.Checkout.PaymentMethods[i],
                            "Invalid PaymentMethodId", this);
                    }
                }
            }

            if (request.Checkout.Expiration != null && request.Checkout.Expiration < TimeSpan.FromSeconds(30.0))
            {
                request.AddModelError(invoiceRequest => invoiceRequest.Checkout.Expiration,
                    "Expiration time must be at least 30 seconds", this);
            }

            if (request.Checkout.PaymentTolerance != null &&
                (request.Checkout.PaymentTolerance < 0 || request.Checkout.PaymentTolerance > 100))
            {
                request.AddModelError(invoiceRequest => invoiceRequest.Checkout.PaymentTolerance,
                    "PaymentTolerance can only be between 0 and 100 percent", this);
            }

            if (request.Checkout.DefaultLanguage != null)
            {
                var lang = LanguageService.FindLanguage(request.Checkout.DefaultLanguage);
                if (lang == null)
                {
                    request.AddModelError(invoiceRequest => invoiceRequest.Checkout.DefaultLanguage,
                    "The requested defaultLang does not exists, Browse the ~/misc/lang page of your BTCPay Server instance to see the list of supported languages.", this);
                }
                else
                {
                    // Ensure this is good case
                    request.Checkout.DefaultLanguage = lang.Code;
                }
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            
            try
            {
                var invoice = await _invoiceController.CreateInvoiceCoreRaw(request, store,
                    Request.GetAbsoluteRoot());
                return Ok(ToModel(invoice));
            }
            catch (BitpayHttpException e)
            {
                return this.CreateAPIError(null, e.Message);
            }
        }

        [Authorize(Policy = Policies.CanModifyInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices/{invoiceId}/status")]
        public async Task<IActionResult> MarkInvoiceStatus(string storeId, string invoiceId,
            MarkInvoiceStatusRequest request)
        {
            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (!BelongsToThisStore(invoice))
                return InvoiceNotFound();

            if (!await _invoiceRepository.MarkInvoiceStatus(invoice.Id, request.Status))
            {
                ModelState.AddModelError(nameof(request.Status),
                    "Status can only be marked to invalid or settled within certain conditions.");
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            return await GetInvoice(storeId, invoiceId);
        }

        [Authorize(Policy = Policies.CanModifyInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices/{invoiceId}/unarchive")]
        public async Task<IActionResult> UnarchiveInvoice(string storeId, string invoiceId)
        {
            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (!BelongsToThisStore(invoice))
                return InvoiceNotFound();

            if (!invoice.Archived)
            {
                return this.CreateAPIError("already-unarchived", "Invoice is already unarchived");
            }


            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            await _invoiceRepository.ToggleInvoiceArchival(invoiceId, false, storeId);
            return await GetInvoice(storeId, invoiceId);
        }

        [Authorize(Policy = Policies.CanViewInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/invoices/{invoiceId}/payment-methods")]
        public async Task<IActionResult> GetInvoicePaymentMethods(string storeId, string invoiceId, bool onlyAccountedPayments = true, bool includeSensitive = false)
        {
            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (!BelongsToThisStore(invoice))
                return InvoiceNotFound();

            if (includeSensitive && !await _authorizationService.CanModifyStore(User))
                return this.CreateAPIPermissionError(Policies.CanModifyStoreSettings);

            return Ok(ToPaymentMethodModels(invoice, onlyAccountedPayments, includeSensitive));
        }

        bool BelongsToThisStore([NotNullWhen(true)] InvoiceEntity invoice) => BelongsToThisStore(invoice, out _);
        private bool BelongsToThisStore([NotNullWhen(true)] InvoiceEntity invoice, [MaybeNullWhen(false)] out Data.StoreData store)
        {
            store = this.HttpContext.GetStoreData();
            return invoice?.StoreId is not null && store.Id == invoice.StoreId;
        }

        [Authorize(Policy = Policies.CanViewInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices/{invoiceId}/payment-methods/{paymentMethod}/activate")]
        public async Task<IActionResult> ActivateInvoicePaymentMethod(string storeId, string invoiceId, string paymentMethod)
        {
            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (!BelongsToThisStore(invoice))
                return InvoiceNotFound();

            if (PaymentMethodId.TryParse(paymentMethod, out var paymentMethodId))
            {
                await _invoiceActivator.ActivateInvoicePaymentMethod(invoiceId, paymentMethodId);
                return Ok();
            }
            ModelState.AddModelError(nameof(paymentMethod), "Invalid payment method");
            return this.CreateValidationError(ModelState);
        }

        [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices/{invoiceId}/refund")]
        public async Task<IActionResult> RefundInvoice(
            string storeId,
            string invoiceId,
            RefundInvoiceRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (!BelongsToThisStore(invoice, out var store))
                return InvoiceNotFound();
            if (!invoice.GetInvoiceState().CanRefund())
            {
                return this.CreateAPIError("non-refundable", "Cannot refund this invoice");
            }
            PaymentPrompt? paymentPrompt = null;
            PayoutMethodId? payoutMethodId = null;
            if (request.PayoutMethodId is null)
                request.PayoutMethodId = invoice.GetDefaultPaymentMethodId(store, _networkProvider)?.ToString();

            if (request.PayoutMethodId is not null && PayoutMethodId.TryParse(request.PayoutMethodId, out payoutMethodId))
            {
                var supported = _payoutHandlers.GetSupportedPayoutMethods(store);
                if (supported.Contains(payoutMethodId))
                {
                    var paymentMethodId = invoice.GetClosestPaymentMethodId([payoutMethodId]);
                    paymentPrompt = paymentMethodId is null ? null : invoice.GetPaymentPrompt(paymentMethodId);
                }
            }
            if (paymentPrompt is null)
            {
                ModelState.AddModelError(nameof(request.PayoutMethodId), "Please select one of the payment methods which were available for the original invoice");
            }
            if (request.RefundVariant is null)
                ModelState.AddModelError(nameof(request.RefundVariant), "`refundVariant` is mandatory");
            if (!ModelState.IsValid || paymentPrompt is null || payoutMethodId is null)
                return this.CreateValidationError(ModelState);

            var accounting = paymentPrompt.Calculate();
            var cryptoPaid = accounting.Paid;
            var dueAmount = accounting.TotalDue;

            // If no payment, but settled and marked, assume it has been fully paid
            if (cryptoPaid is 0 && invoice is { Status: InvoiceStatus.Settled, ExceptionStatus: InvoiceExceptionStatus.Marked })
            {
                cryptoPaid = accounting.TotalDue;
                dueAmount = 0;
            }
            var cdCurrency = _currencyNameTable.GetCurrencyData(invoice.Currency, true);
            var paidCurrency = Math.Round(cryptoPaid * paymentPrompt.Rate, cdCurrency.Divisibility);
            var rateResult = await _rateProvider.FetchRate(
                new CurrencyPair(paymentPrompt.Currency, invoice.Currency),
                store.GetStoreBlob().GetRateRules(_defaultRules), new StoreIdRateContext(storeId),

				cancellationToken
            );
            var paidAmount = cryptoPaid.RoundToSignificant(paymentPrompt.Divisibility);
            var createPullPayment = new CreatePullPaymentRequest
            {
                Name = request.Name ?? $"Refund {invoice.Id}",
                Description = request.Description,
                PayoutMethods = new[] { payoutMethodId.ToString() },
            };

            if (request.RefundVariant != RefundVariant.Custom)
            {
                if (request.CustomAmount is not null)
                    ModelState.AddModelError(nameof(request.CustomAmount), "CustomAmount should only be set if the refundVariant is Custom");
                if (request.CustomCurrency is not null)
                    ModelState.AddModelError(nameof(request.CustomCurrency), "CustomCurrency should only be set if the refundVariant is Custom");
            }
            if (request.SubtractPercentage is < 0 or > 100)
            {
                ModelState.AddModelError(nameof(request.SubtractPercentage), "Percentage must be a numeric value between 0 and 100");
            }
            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            var appliedDivisibility = paymentPrompt.Divisibility;
            switch (request.RefundVariant)
            {
                case RefundVariant.RateThen:
                    createPullPayment.Currency = paymentPrompt.Currency;
                    createPullPayment.Amount = paidAmount;
                    createPullPayment.AutoApproveClaims = true;
                    break;

                case RefundVariant.CurrentRate:
                    createPullPayment.Currency = paymentPrompt.Currency;
                    createPullPayment.Amount = Math.Round(paidCurrency / rateResult.BidAsk.Bid, appliedDivisibility);
                    createPullPayment.AutoApproveClaims = true;
                    break;

                case RefundVariant.Fiat:
                    appliedDivisibility = cdCurrency.Divisibility;
                    createPullPayment.Currency = invoice.Currency;
                    createPullPayment.Amount = paidCurrency;
                    createPullPayment.AutoApproveClaims = false;
                    break;

                case RefundVariant.OverpaidAmount:
                    if (invoice.ExceptionStatus != InvoiceExceptionStatus.PaidOver)
                    {
                        ModelState.AddModelError(nameof(request.RefundVariant), "Invoice is not overpaid");
                    }
                    if (!ModelState.IsValid)
                    {
                        return this.CreateValidationError(ModelState);
                    }
                    createPullPayment.Currency = paymentPrompt.Currency;
                    createPullPayment.Amount = Math.Round(paidAmount - dueAmount, appliedDivisibility);
                    createPullPayment.AutoApproveClaims = true;
                    break;

                case RefundVariant.Custom:
                    if (request.CustomAmount is null || (request.CustomAmount is decimal v && v <= 0))
                    {
                        ModelState.AddModelError(nameof(request.CustomAmount), "Amount must be greater than 0");
                    }

                    if (
                        string.IsNullOrEmpty(request.CustomCurrency) ||
                        _currencyNameTable.GetCurrencyData(request.CustomCurrency, false) == null
                    )
                    {
                        ModelState.AddModelError(nameof(request.CustomCurrency), "Invalid currency");
                    }

                    if (rateResult.BidAsk is null)
                    {
                        ModelState.AddModelError(nameof(request.RefundVariant),
                            $"Impossible to fetch rate: {rateResult.EvaluatedRule}");
                    }

                    if (!ModelState.IsValid || request.CustomAmount is null)
                    {
                        return this.CreateValidationError(ModelState);
                    }

                    createPullPayment.Currency = request.CustomCurrency;
                    createPullPayment.Amount = request.CustomAmount.Value;
                    createPullPayment.AutoApproveClaims = paymentPrompt.Currency == request.CustomCurrency;
                    break;

                default:
                    ModelState.AddModelError(nameof(request.RefundVariant), "Please select a valid refund option");
                    return this.CreateValidationError(ModelState);
            }
            
            // reduce by percentage
            if (request.SubtractPercentage is > 0 and <= 100)
            {
                var reduceByAmount = createPullPayment.Amount * (request.SubtractPercentage / 100);
                createPullPayment.Amount = Math.Round(createPullPayment.Amount - reduceByAmount, appliedDivisibility);
            }

            createPullPayment.AutoApproveClaims = createPullPayment.AutoApproveClaims && (await _authorizationService.AuthorizeAsync(User, storeId ,Policies.CanCreatePullPayments)).Succeeded;
            var ppId = await _pullPaymentService.CreatePullPayment(store, createPullPayment);

            await using var ctx = _dbContextFactory.CreateContext();

            ctx.Refunds.Add(new RefundData
            {
                InvoiceDataId = invoice.Id,
                PullPaymentDataId = ppId
            });
            await ctx.SaveChangesAsync(cancellationToken);

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

        private IActionResult InvoiceNotFound()
        {
            return this.CreateAPIError(404, "invoice-not-found", "The invoice was not found");
        }

        private InvoicePaymentMethodDataModel[] ToPaymentMethodModels(InvoiceEntity entity, bool includeAccountedPaymentOnly, bool includeSensitive)
        {
            return entity.GetPaymentPrompts().Select(
                prompt =>
                {
                    _handlers.TryGetValue(prompt.PaymentMethodId, out var handler);
                    var accounting = prompt.Currency is not null ? prompt.Calculate() : null;
                    var payments = prompt.ParentEntity.GetPayments(includeAccountedPaymentOnly).Where(paymentEntity =>
                        paymentEntity.PaymentMethodId == prompt.PaymentMethodId);
                    _paymentLinkExtensions.TryGetValue(prompt.PaymentMethodId, out var paymentLinkExtension);

                    var details = prompt.Details;
                    if (handler is not null && prompt.Activated)
                    {
                        var detailsObj = handler.ParsePaymentPromptDetails(details);
                        if (!includeSensitive)
                            handler.StripDetailsForNonOwner(detailsObj);
                        details = JToken.FromObject(detailsObj, handler.Serializer.ForAPI());
                    }
                    return new InvoicePaymentMethodDataModel
                    {
                        Activated = prompt.Activated,
                        PaymentMethodId = prompt.PaymentMethodId.ToString(),
                        Currency = prompt.Currency,
                        Destination = prompt.Destination,
                        Rate = prompt.Currency is not null ? prompt.Rate : 0m,
                        Due = accounting?.DueUncapped ?? 0m,
                        TotalPaid = accounting?.Paid ?? 0m,
                        PaymentMethodPaid = accounting?.PaymentMethodPaid ?? 0m,
                        Amount = accounting?.TotalDue ?? 0m,
                        PaymentMethodFee = accounting?.PaymentMethodFee ?? 0m,
                        PaymentLink = (prompt.Activated ? paymentLinkExtension?.GetPaymentLink(prompt, Url) : null) ?? string.Empty,
                        Payments = payments.Select(paymentEntity => ToPaymentModel(entity, paymentEntity)).ToList(),
                        AdditionalData = details
                    };
                }).ToArray();
        }

        public static InvoicePaymentMethodDataModel.Payment ToPaymentModel(InvoiceEntity entity, PaymentEntity paymentEntity)
        {
            return new InvoicePaymentMethodDataModel.Payment()
            {
                Destination = paymentEntity.Destination,
                Id = paymentEntity.Id,
                Status = paymentEntity.Status switch
                {
                    PaymentStatus.Processing => InvoicePaymentMethodDataModel.Payment.PaymentStatus.Processing,
                    PaymentStatus.Settled => InvoicePaymentMethodDataModel.Payment.PaymentStatus.Settled,
                    PaymentStatus.Unaccounted => InvoicePaymentMethodDataModel.Payment.PaymentStatus.Invalid,
                    _ => throw new NotSupportedException(paymentEntity.Status.ToString())
                },
                Fee = paymentEntity.PaymentMethodFee,
                Value = paymentEntity.Value,
                ReceivedDate = paymentEntity.ReceivedTime.DateTime
            };
        }

        private InvoiceData ToModel(InvoiceEntity entity)
        {
            return ToModel(entity, _linkGenerator, Request);
        }

        public static InvoiceData ToModel(InvoiceEntity entity, LinkGenerator linkGenerator, HttpRequest? request)
        {
            var statuses = new List<InvoiceStatus>();
            var state = entity.GetInvoiceState();
            if (state.CanMarkComplete())
            {
                statuses.Add(InvoiceStatus.Settled);
            }
            if (state.CanMarkInvalid())
            {
                statuses.Add(InvoiceStatus.Invalid);
            }
            var store = request?.HttpContext.GetStoreData();
            var receipt = store == null ? entity.ReceiptOptions : InvoiceDataBase.ReceiptOptions.Merge(store.GetStoreBlob().ReceiptOptions, entity.ReceiptOptions);
            return new InvoiceData
            {
                StoreId = entity.StoreId,
                ExpirationTime = entity.ExpirationTime,
                MonitoringExpiration = entity.MonitoringExpiration,
                CreatedTime = entity.InvoiceTime,
                Amount = entity.Price,
                Type = entity.Type,
                Id = entity.Id,
                CheckoutLink = request is null ? null : linkGenerator.CheckoutLink(entity.Id, request.Scheme, request.Host, request.PathBase),
                Status = entity.Status,
                AdditionalStatus = entity.ExceptionStatus,
                Currency = entity.Currency,
                Archived = entity.Archived,
                Metadata = entity.Metadata.ToJObject(),
                AvailableStatusesForManualMarking = statuses.ToArray(),
                Checkout = new InvoiceDataBase.CheckoutOptions
                {
                    Expiration = entity.ExpirationTime - entity.InvoiceTime,
                    Monitoring = entity.MonitoringExpiration - entity.ExpirationTime,
                    PaymentTolerance = entity.PaymentTolerance,
                    PaymentMethods =
                        entity.GetPaymentPrompts().Select(method => method.PaymentMethodId.ToString()).ToArray(),
                    DefaultPaymentMethod = entity.DefaultPaymentMethod?.ToString(),
                    SpeedPolicy = entity.SpeedPolicy,
                    DefaultLanguage = entity.DefaultLanguage,
                    RedirectAutomatically = entity.RedirectAutomatically,
                    RedirectURL = entity.RedirectURLTemplate
                },
                Receipt = receipt
            };
        }
    }
}
