using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        public GreenFieldInvoiceController(InvoiceController invoiceController, InvoiceRepository invoiceRepository)
        {
            _invoiceController = invoiceController;
            _invoiceRepository = invoiceRepository;
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
                    StoreId = new[] {store.Id}, IncludeArchived = includeArchived
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
            if (invoice.StoreId != store.Id)
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

            if (!string.IsNullOrEmpty(request.CustomerEmail) &&
                !EmailValidator.IsEmail(request.CustomerEmail))
            {
                request.AddModelError(invoiceRequest => invoiceRequest.CustomerEmail, "Invalid email address",
                    this);
            }

            if (request.Checkout.ExpirationTime != null && request.Checkout.ExpirationTime < DateTime.Now)
            {
                request.AddModelError(invoiceRequest => invoiceRequest.Checkout.ExpirationTime,
                    "Expiration time must be in the future", this);
            }

            if (request.Checkout.PaymentTolerance != null &&
                (request.Checkout.PaymentTolerance < 0 || request.Checkout.PaymentTolerance > 100))
            {
                request.AddModelError(invoiceRequest => invoiceRequest.Checkout.PaymentTolerance,
                    "PaymentTolerance can only be between 0 and 100 percent", this);
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            try
            {
                var invoice = await _invoiceController.CreateInvoiceCoreRaw(FromModel(request), store,
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
        [HttpPut("~/api/v1/stores/{storeId}/invoices/{invoiceId}")]
        public async Task<IActionResult> UpdateInvoice(string storeId, string invoiceId, UpdateInvoiceRequest request)
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

            if (request.Archived.HasValue)
            {
                if (request.Archived.Value && !invoice.Archived)
                {
                    ModelState.AddModelError(nameof(request.Archived),
                        "You can only archive an invoice via HTTP DELETE.");
                }
                else if (!request.Archived.Value && invoice.Archived)
                {
                    await _invoiceRepository.ToggleInvoiceArchival(invoiceId, false, storeId);
                }
            }

            if (request.Status != null)
            {
                if (!await _invoiceRepository.MarkInvoiceStatus(invoice.Id, request.Status.Value))
                {
                    ModelState.AddModelError(nameof(request.Status),
                        "Status can only be marked to invalid or complete within certain conditions.");
                }
            }

            if (request.Email != null)
            {
                if (!EmailValidator.IsEmail(request.Email))
                {
                    request.AddModelError(invoiceRequest => invoiceRequest.Email, "Invalid email address",
                        this);
                }
                else if (!string.IsNullOrEmpty(invoice.BuyerInformation.BuyerEmail))
                {
                    request.AddModelError(invoiceRequest => invoiceRequest.Email, "Email address already set",
                        this);
                }

                await _invoiceRepository.UpdateInvoice(invoice.Id, new UpdateCustomerModel() {Email = request.Email});
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            return await GetInvoice(storeId, invoiceId);
        }

        private InvoiceData ToModel(InvoiceEntity entity)
        {
            return new InvoiceData()
            {
                Amount = entity.ProductInformation.Price,
                Id = entity.Id,
                Status = entity.Status,
                ExceptionStatus = entity.ExceptionStatus,
                Currency = entity.ProductInformation.Currency,
                Metadata = entity.PosData,
                CustomerEmail = entity.RefundMail ?? entity.BuyerInformation.BuyerEmail,
                Checkout = new CreateInvoiceRequest.CheckoutOptions()
                {
                    ExpirationTime = entity.ExpirationTime,
                    PaymentTolerance = entity.PaymentTolerance,
                    PaymentMethods =
                        entity.GetPaymentMethods().Select(method => method.GetId().ToString()).ToArray(),
                    RedirectAutomatically = entity.RedirectAutomatically,
                    RedirectUri = entity.RedirectURL?.ToString(),
                    SpeedPolicy = entity.SpeedPolicy,
                    WebHook = entity.NotificationURL
                },
                PaymentMethodData = entity.GetPaymentMethods().ToDictionary(method => method.GetId().ToString(),
                    method =>
                    {
                        var accounting = method.Calculate();
                        var details = method.GetPaymentMethodDetails();
                        var payments = method.ParentEntity.GetPayments().Where(paymentEntity =>
                            paymentEntity.GetPaymentMethodId() == method.GetId());

                        return new InvoiceData.PaymentMethodDataModel()
                        {
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
                                return new InvoiceData.PaymentMethodDataModel.Payment()
                                {
                                    Destination = data.GetDestination(),
                                    Id = data.GetPaymentId(),
                                    Status = !paymentEntity.Accounted
                                        ? InvoiceData.PaymentMethodDataModel.Payment.PaymentStatus.Invalid
                                        : data.PaymentCompleted(paymentEntity)
                                            ? InvoiceData.PaymentMethodDataModel.Payment.PaymentStatus.Complete
                                            : data.PaymentConfirmed(paymentEntity, entity.SpeedPolicy)
                                                ? InvoiceData.PaymentMethodDataModel.Payment.PaymentStatus
                                                    .AwaitingCompletion
                                                : InvoiceData.PaymentMethodDataModel.Payment.PaymentStatus
                                                    .AwaitingConfirmation,
                                    Fee = paymentEntity.NetworkFee,
                                    Value = data.GetValue(),
                                    ReceivedDate = paymentEntity.ReceivedTime.DateTime
                                };
                            }).ToList()
                        };
                    })
            };
        }

        private Models.CreateInvoiceRequest FromModel(CreateInvoiceRequest entity)
        {
            Buyer buyer = null;
            ProductInformation pi = null;
            JToken? orderId = null;
            if (!string.IsNullOrEmpty(entity.Metadata) && entity.Metadata.StartsWith('{'))
            {
                //metadata was provided and is json. Let's try and match props
                try
                {
                    buyer = JsonConvert.DeserializeObject<Buyer>(entity.Metadata);
                    pi = JsonConvert.DeserializeObject<ProductInformation>(entity.Metadata);
                    JObject.Parse(entity.Metadata).TryGetValue("orderid", StringComparison.InvariantCultureIgnoreCase,
                        out orderId);
                }
                catch
                {
                    // ignored
                }
            }
            return new Models.CreateInvoiceRequest()
            {
                Buyer = buyer,
                BuyerEmail = entity.CustomerEmail,
                Currency = entity.Currency,
                Price = entity.Amount,
                Refundable = true,
                ExtendedNotifications = true,
                FullNotifications = true,
                RedirectURL = entity.Checkout.RedirectUri,
                RedirectAutomatically = entity.Checkout.RedirectAutomatically,
                ExpirationTime = entity.Checkout.ExpirationTime,
                TransactionSpeed = entity.Checkout.SpeedPolicy?.ToString(),
                PaymentCurrencies = entity.Checkout.PaymentMethods,
                NotificationURL = entity.Checkout.RedirectUri,
                PosData = entity.Metadata,
                Physical = pi?.Physical??false,
                ItemCode = pi?.ItemCode,
                ItemDesc = pi?.ItemDesc,
                TaxIncluded = pi?.TaxIncluded,
                OrderId = orderId?.ToString()
            };
        }
    }
}
