using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NBitcoin;
using CreateInvoiceRequest = BTCPayServer.Client.Models.CreateInvoiceRequest;
using InvoiceData = BTCPayServer.Client.Models.InvoiceData;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenFieldInvoiceController : Controller
    {
        private readonly InvoiceController _invoiceController;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly LinkGenerator _linkGenerator;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly EventAggregator _eventAggregator;
        private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;

        public LanguageService LanguageService { get; }

        public GreenFieldInvoiceController(InvoiceController invoiceController, InvoiceRepository invoiceRepository,
            LinkGenerator linkGenerator, LanguageService languageService, BTCPayNetworkProvider btcPayNetworkProvider,
            EventAggregator eventAggregator, PaymentMethodHandlerDictionary paymentMethodHandlerDictionary)
        {
            _invoiceController = invoiceController;
            _invoiceRepository = invoiceRepository;
            _linkGenerator = linkGenerator;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _eventAggregator = eventAggregator;
            _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
            LanguageService = languageService;
        }

        [Authorize(Policy = Policies.CanViewInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/invoices")]
        public async Task<IActionResult> GetInvoices(string storeId, bool includeArchived = false)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            var invoices =
                await _invoiceRepository.GetInvoices(new InvoiceQuery()
                {
                    StoreId = new[] { store.Id },
                    IncludeArchived = includeArchived
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
                return NotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice?.StoreId != store.Id)
            {
                return NotFound();
            }

            return Ok(ToModel(invoice));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/invoices/{invoiceId}")]
        public async Task<IActionResult> ArchiveInvoice(string storeId, string invoiceId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            await _invoiceRepository.ToggleInvoiceArchival(invoiceId, true, storeId);
            return Ok();
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/invoices/{invoiceId}")]
        public async Task<IActionResult> UpdateInvoice(string storeId, string invoiceId, UpdateInvoiceRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            var result = await _invoiceRepository.UpdateInvoiceMetadata(invoiceId, storeId, request.Metadata);
            if (result != null)
            {
                return Ok(ToModel(result));
            }

            return NotFound();
        }

        [Authorize(Policy = Policies.CanCreateInvoice,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices")]
        public async Task<IActionResult> CreateInvoice(string storeId, CreateInvoiceRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            if (request.Amount < 0.0m)
            {
                ModelState.AddModelError(nameof(request.Amount), "The amount should be 0 or more.");
            }

            if (string.IsNullOrEmpty(request.Currency))
            {
                ModelState.AddModelError(nameof(request.Currency), "Currency is required");
            }
            request.Checkout = request.Checkout ?? new CreateInvoiceRequest.CheckoutOptions();
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
                var lang = LanguageService.FindBestMatch(request.Checkout.DefaultLanguage);
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
                    Request.GetAbsoluteUri(""));
                return Ok(ToModel(invoice));
            }
            catch (BitpayHttpException e)
            {
                return this.CreateAPIError(null, e.Message);
            }
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices/{invoiceId}/status")]
        public async Task<IActionResult> MarkInvoiceStatus(string storeId, string invoiceId,
            MarkInvoiceStatusRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice.StoreId != store.Id)
            {
                return NotFound();
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

        [Authorize(Policy = Policies.CanModifyStoreSettings,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices/{invoiceId}/unarchive")]
        public async Task<IActionResult> UnarchiveInvoice(string storeId, string invoiceId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice.StoreId != store.Id)
            {
                return NotFound();
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
        public async Task<IActionResult> GetInvoicePaymentMethods(string storeId, string invoiceId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice?.StoreId != store.Id)
            {
                return NotFound();
            }

            return Ok(ToPaymentMethodModels(invoice));
        }

        [Authorize(Policy = Policies.CanViewInvoices,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices/{invoiceId}/payment-methods/{paymentMethod}/activate")]
        public async Task<IActionResult> ActivateInvoicePaymentMethod(string storeId, string invoiceId, string paymentMethod)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
            if (invoice?.StoreId != store.Id)
            {
                return NotFound();
            }

            if (PaymentMethodId.TryParse(paymentMethod, out var paymentMethodId))
            {
                await _invoiceRepository.ActivateInvoicePaymentMethod(_eventAggregator, _btcPayNetworkProvider,
                    _paymentMethodHandlerDictionary, store, invoice, paymentMethodId);
                return Ok();
            }
            return BadRequest();
        }
        
        private InvoicePaymentMethodDataModel[] ToPaymentMethodModels(InvoiceEntity entity)
        {
            return entity.GetPaymentMethods().Select(
                method =>
                {
                    var accounting = method.Calculate();
                    var details = method.GetPaymentMethodDetails();
                    var payments = method.ParentEntity.GetPayments().Where(paymentEntity =>
                        paymentEntity.GetPaymentMethodId() == method.GetId());

                    return new InvoicePaymentMethodDataModel()
                    {
                        Activated = details.Activated,
                        PaymentMethod = method.GetId().ToStringNormalized(),
                        Destination = details.GetPaymentDestination(),
                        Rate = method.Rate,
                        Due = accounting.Due.ToDecimal(MoneyUnit.BTC),
                        TotalPaid = accounting.Paid.ToDecimal(MoneyUnit.BTC),
                        PaymentMethodPaid = accounting.CryptoPaid.ToDecimal(MoneyUnit.BTC),
                        Amount = accounting.Due.ToDecimal(MoneyUnit.BTC),
                        NetworkFee = accounting.NetworkFee.ToDecimal(MoneyUnit.BTC),
                        PaymentLink =
                            method.GetId().PaymentType.GetPaymentLink(method.Network, details, accounting.Due,
                                Request.GetAbsoluteRoot()),
                        Payments = payments.Select(paymentEntity =>
                        {
                            var data = paymentEntity.GetCryptoPaymentData();
                            return new InvoicePaymentMethodDataModel.Payment()
                            {
                                Destination = data.GetDestination(),
                                Id = data.GetPaymentId(),
                                Status = !paymentEntity.Accounted
                                    ? InvoicePaymentMethodDataModel.Payment.PaymentStatus.Invalid
                                    : data.PaymentConfirmed(paymentEntity, entity.SpeedPolicy) ||
                                      data.PaymentCompleted(paymentEntity)
                                        ? InvoicePaymentMethodDataModel.Payment.PaymentStatus.Settled
                                        : InvoicePaymentMethodDataModel.Payment.PaymentStatus.Processing,
                                Fee = paymentEntity.NetworkFee,
                                Value = data.GetValue(),
                                ReceivedDate = paymentEntity.ReceivedTime.DateTime
                            };
                        }).ToList()
                    };
                }).ToArray();
        }
        private InvoiceData ToModel(InvoiceEntity entity)
        {
            return new InvoiceData()
            {
                ExpirationTime = entity.ExpirationTime,
                MonitoringExpiration = entity.MonitoringExpiration,
                CreatedTime = entity.InvoiceTime,
                Amount = entity.Price,
                Id = entity.Id,
                CheckoutLink = _linkGenerator.CheckoutLink(entity.Id, Request.Scheme, Request.Host, Request.PathBase),
                Status = entity.Status.ToModernStatus(),
                AdditionalStatus = entity.ExceptionStatus,
                Currency = entity.Currency,
                Metadata = entity.Metadata.ToJObject(),
                Checkout = new CreateInvoiceRequest.CheckoutOptions()
                {
                    Expiration = entity.ExpirationTime - entity.InvoiceTime,
                    Monitoring = entity.MonitoringExpiration - entity.ExpirationTime,
                    PaymentTolerance = entity.PaymentTolerance,
                    PaymentMethods =
                        entity.GetPaymentMethods().Select(method => method.GetId().ToStringNormalized()).ToArray(),
                    SpeedPolicy = entity.SpeedPolicy,
                    DefaultLanguage = entity.DefaultLanguage,
                    RedirectAutomatically = entity.RedirectAutomatically,
                    RedirectURL = entity.RedirectURLTemplate
                }
            };
        }
    }
}
