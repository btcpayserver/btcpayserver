#nullable enable
using System;
using System.Collections.Generic;
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
using NBitcoin;
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
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly PullPaymentHostedService _pullPaymentService;
        private readonly RateFetcher _rateProvider;
        private readonly InvoiceActivator _invoiceActivator;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly IAuthorizationService _authorizationService;

        public LanguageService LanguageService { get; }

        public GreenfieldInvoiceController(UIInvoiceController invoiceController, InvoiceRepository invoiceRepository,
            LinkGenerator linkGenerator, LanguageService languageService, BTCPayNetworkProvider btcPayNetworkProvider,
            CurrencyNameTable currencyNameTable, RateFetcher rateProvider,
            InvoiceActivator invoiceActivator,
            PullPaymentHostedService pullPaymentService, 
            ApplicationDbContextFactory dbContextFactory, 
            IAuthorizationService authorizationService)
        {
            _invoiceController = invoiceController;
            _invoiceRepository = invoiceRepository;
            _linkGenerator = linkGenerator;
            _currencyNameTable = currencyNameTable;
            _networkProvider = btcPayNetworkProvider;
            _rateProvider = rateProvider;
            _invoiceActivator = invoiceActivator;
            _pullPaymentService = pullPaymentService;
            _dbContextFactory = dbContextFactory;
            _authorizationService = authorizationService;
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
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return StoreNotFound();
            }
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
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return InvoiceNotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice?.StoreId != store.Id)
            {
                return InvoiceNotFound();
            }

            return Ok(ToModel(invoice));
        }

        [Authorize(Policy = Policies.CanModifyInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/invoices/{invoiceId}")]
        public async Task<IActionResult> ArchiveInvoice(string storeId, string invoiceId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return InvoiceNotFound();
            }
            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice?.StoreId != store.Id)
            {
                return InvoiceNotFound();
            }
            await _invoiceRepository.ToggleInvoiceArchival(invoiceId, true, storeId);
            return Ok();
        }

        [Authorize(Policy = Policies.CanModifyInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/invoices/{invoiceId}")]
        public async Task<IActionResult> UpdateInvoice(string storeId, string invoiceId, UpdateInvoiceRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return InvoiceNotFound();
            }

            var result = await _invoiceRepository.UpdateInvoiceMetadata(invoiceId, storeId, request.Metadata);
            if (result != null)
            {
                return Ok(ToModel(result));
            }

            return InvoiceNotFound();
        }

        [Authorize(Policy = Policies.CanCreateInvoice,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices")]
        public async Task<IActionResult> CreateInvoice(string storeId, CreateInvoiceRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return StoreNotFound();
            }

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
                    if (!PaymentMethodId.TryParse(request.Checkout.PaymentMethods[i], out _))
                    {
                        request.AddModelError(invoiceRequest => invoiceRequest.Checkout.PaymentMethods[i],
                            "Invalid payment method", this);
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
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return InvoiceNotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice.StoreId != store.Id)
            {
                return InvoiceNotFound();
            }

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
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return InvoiceNotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice.StoreId != store.Id)
            {
                return InvoiceNotFound();
            }

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
        public async Task<IActionResult> GetInvoicePaymentMethods(string storeId, string invoiceId, bool onlyAccountedPayments = true)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return InvoiceNotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice?.StoreId != store.Id)
            {
                return InvoiceNotFound();
            }

            return Ok(ToPaymentMethodModels(invoice, onlyAccountedPayments));
        }

        [Authorize(Policy = Policies.CanViewInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices/{invoiceId}/payment-methods/{paymentMethod}/activate")]
        public async Task<IActionResult> ActivateInvoicePaymentMethod(string storeId, string invoiceId, string paymentMethod)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return InvoiceNotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice?.StoreId != store.Id)
            {
                return InvoiceNotFound();
            }

            if (PaymentMethodId.TryParse(paymentMethod, out var paymentMethodId))
            {
                await _invoiceActivator.ActivateInvoicePaymentMethod(paymentMethodId, invoice, store);
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
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return StoreNotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice == null)
            {
                return InvoiceNotFound();
            }

            if (invoice.StoreId != store.Id)
            {
                return InvoiceNotFound();
            }
            if (!invoice.GetInvoiceState().CanRefund())
            {
                return this.CreateAPIError("non-refundable", "Cannot refund this invoice");
            }
            PaymentMethod? invoicePaymentMethod = null;
            PaymentMethodId? paymentMethodId = null;
            if (request.PaymentMethod is not null && PaymentMethodId.TryParse(request.PaymentMethod, out paymentMethodId))
            {
                invoicePaymentMethod = invoice.GetPaymentMethods().SingleOrDefault(method => method.GetId() == paymentMethodId);
            }
            if (invoicePaymentMethod is null)
            {
                ModelState.AddModelError(nameof(request.PaymentMethod), "Please select one of the payment methods which were available for the original invoice");
            }
            if (request.RefundVariant is null)
                ModelState.AddModelError(nameof(request.RefundVariant), "`refundVariant` is mandatory");
            if (!ModelState.IsValid || invoicePaymentMethod is null || paymentMethodId is null)
                return this.CreateValidationError(ModelState);

            var accounting = invoicePaymentMethod.Calculate();
            var cryptoPaid = accounting.Paid;
            var dueAmount = accounting.TotalDue;

            // If no payment, but settled and marked, assume it has been fully paid
            if (cryptoPaid is 0 && invoice is { Status: InvoiceStatusLegacy.Confirmed or InvoiceStatusLegacy.Complete, ExceptionStatus: InvoiceExceptionStatus.Marked })
            {
                cryptoPaid = accounting.TotalDue;
                dueAmount = 0;
            }
            var cdCurrency = _currencyNameTable.GetCurrencyData(invoice.Currency, true);
            var paidCurrency = Math.Round(cryptoPaid * invoicePaymentMethod.Rate, cdCurrency.Divisibility);
            var rateResult = await _rateProvider.FetchRate(
                new CurrencyPair(paymentMethodId.CryptoCode, invoice.Currency),
                store.GetStoreBlob().GetRateRules(_networkProvider),
                cancellationToken
            );
            var cryptoCode = invoicePaymentMethod.GetId().CryptoCode;
            var paymentMethodDivisibility = _currencyNameTable.GetCurrencyData(paymentMethodId.CryptoCode, false)?.Divisibility ?? 8;
            var paidAmount = cryptoPaid.RoundToSignificant(paymentMethodDivisibility);
            var createPullPayment = new CreatePullPayment
            {
                BOLT11Expiration = store.GetStoreBlob().RefundBOLT11Expiration,
                Name = request.Name ?? $"Refund {invoice.Id}",
                Description = request.Description,
                StoreId = storeId,
                PaymentMethodIds = new[] { paymentMethodId },
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

            var appliedDivisibility = paymentMethodDivisibility;
            switch (request.RefundVariant)
            {
                case RefundVariant.RateThen:
                    createPullPayment.Currency = cryptoCode;
                    createPullPayment.Amount = paidAmount;
                    createPullPayment.AutoApproveClaims = true;
                    break;

                case RefundVariant.CurrentRate:
                    createPullPayment.Currency = cryptoCode;
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
                    
                    createPullPayment.Currency = cryptoCode;
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
                    createPullPayment.AutoApproveClaims = paymentMethodId.CryptoCode == request.CustomCurrency;
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

            createPullPayment.AutoApproveClaims = createPullPayment.AutoApproveClaims && (await _authorizationService.AuthorizeAsync(User, createPullPayment.StoreId ,Policies.CanCreatePullPayments)).Succeeded;
            var ppId = await _pullPaymentService.CreatePullPayment(createPullPayment);

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
                Amount = ppBlob.Limit,
                Name = ppBlob.Name,
                Description = ppBlob.Description,
                Currency = ppBlob.Currency,
                Period = ppBlob.Period,
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
        private IActionResult StoreNotFound()
        {
            return this.CreateAPIError(404, "store-not-found", "The store was not found");
        }

        private InvoicePaymentMethodDataModel[] ToPaymentMethodModels(InvoiceEntity entity, bool includeAccountedPaymentOnly)
        {
            return entity.GetPaymentMethods().Select(
                method =>
                {
                    var accounting = method.Calculate();
                    var details = method.GetPaymentMethodDetails();
                    var payments = method.ParentEntity.GetPayments(includeAccountedPaymentOnly).Where(paymentEntity =>
                        paymentEntity.GetPaymentMethodId() == method.GetId());

                    return new InvoicePaymentMethodDataModel
                    {
                        Activated = details.Activated,
                        PaymentMethod = method.GetId().ToStringNormalized(),
                        CryptoCode = method.GetId().CryptoCode,
                        Destination = details.GetPaymentDestination(),
                        Rate = method.Rate,
                        Due = accounting.DueUncapped,
                        TotalPaid = accounting.Paid,
                        PaymentMethodPaid = accounting.CryptoPaid,
                        Amount = accounting.TotalDue,
                        NetworkFee = accounting.NetworkFee,
                        PaymentLink =
                            method.GetId().PaymentType.GetPaymentLink(method.Network, entity, details, accounting.Due,
                                Request.GetAbsoluteRoot()),
                        Payments = payments.Select(paymentEntity => ToPaymentModel(entity, paymentEntity)).ToList(),
                        AdditionalData = details.GetAdditionalData()
                    };
                }).ToArray();
        }

        public static InvoicePaymentMethodDataModel.Payment ToPaymentModel(InvoiceEntity entity, PaymentEntity paymentEntity)
        {
            var data = paymentEntity.GetCryptoPaymentData();
            return new InvoicePaymentMethodDataModel.Payment()
            {
                Destination = data.GetDestination(),
                Id = data.GetPaymentId(),
                Status = !paymentEntity.Accounted
                    ? InvoicePaymentMethodDataModel.Payment.PaymentStatus.Invalid
                    : data.PaymentConfirmed(paymentEntity, entity.SpeedPolicy) || data.PaymentCompleted(paymentEntity)
                        ? InvoicePaymentMethodDataModel.Payment.PaymentStatus.Settled
                        : InvoicePaymentMethodDataModel.Payment.PaymentStatus.Processing,
                Fee = paymentEntity.NetworkFee,
                Value = data.GetValue(),
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
            return new InvoiceData()
            {
                StoreId = entity.StoreId,
                ExpirationTime = entity.ExpirationTime,
                MonitoringExpiration = entity.MonitoringExpiration,
                CreatedTime = entity.InvoiceTime,
                Amount = entity.Price,
                Type = entity.Type,
                Id = entity.Id,
                CheckoutLink = request is null ? null : linkGenerator.CheckoutLink(entity.Id, request.Scheme, request.Host, request.PathBase),
                Status = entity.Status.ToModernStatus(),
                AdditionalStatus = entity.ExceptionStatus,
                Currency = entity.Currency,
                Archived = entity.Archived,
                Metadata = entity.Metadata.ToJObject(),
                AvailableStatusesForManualMarking = statuses.ToArray(),
                Checkout = new CreateInvoiceRequest.CheckoutOptions()
                {
                    Expiration = entity.ExpirationTime - entity.InvoiceTime,
                    Monitoring = entity.MonitoringExpiration - entity.ExpirationTime,
                    PaymentTolerance = entity.PaymentTolerance,
                    PaymentMethods =
                        entity.GetPaymentMethods().Select(method => method.GetId().ToStringNormalized()).ToArray(),
                    DefaultPaymentMethod = entity.DefaultPaymentMethod,
                    SpeedPolicy = entity.SpeedPolicy,
                    DefaultLanguage = entity.DefaultLanguage,
                    RedirectAutomatically = entity.RedirectAutomatically,
                    RequiresRefundEmail = entity.RequiresRefundEmail,
                    CheckoutType = entity.CheckoutType,
                    RedirectURL = entity.RedirectURLTemplate
                },
                Receipt = entity.ReceiptOptions
            };
        }
    }
}
