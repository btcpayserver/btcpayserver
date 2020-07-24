using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
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
        [HttpGet("~/api/v1/stores/{storeId}/invoices/{invoiceId}")]
        public async Task<IActionResult> GetInvoices(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            var invoices =
                await _invoiceRepository.GetInvoices(new InvoiceQuery()
                {
                    StoreId = new[] {store.Id}, IncludeArchived = false
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

            if (!string.IsNullOrEmpty(request.Customer.BuyerEmail) &&
                !EmailValidator.IsEmail(request.Customer.BuyerEmail))
            {
                request.AddModelError(invoiceRequest => invoiceRequest.Customer.BuyerEmail, "Invalid email address",
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

            var invoice = await _invoiceController.CreateInvoiceCoreRaw(FromModel(request), store,
                Request.GetAbsoluteUri(""));
            return Ok(ToModel(invoice));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/invoices/{invoiceId}")]
        public async Task<IActionResult> UpdateInvoice(string storeId, string invoiceId, UpdateInvoiceRequest request)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                return NotFound();
            }

            await _invoiceRepository.ToggleInvoiceArchival(invoiceId, request.Archived, storeId);
            return await GetInvoice(storeId, invoiceId);
        }

        public InvoiceData ToModel(InvoiceEntity entity)
        {
            return new InvoiceData()
            {
                Amount = entity.ProductInformation.Price,
                Id = entity.Id,
                Currency = entity.ProductInformation.Currency,
                Metadata =
                    new CreateInvoiceRequest.ProductInformation()
                    {
                        Physical = entity.ProductInformation.Physical,
                        ItemCode = entity.ProductInformation.ItemCode,
                        ItemDesc = entity.ProductInformation.ItemDesc,
                        OrderId = entity.OrderId,
                        PosData = entity.PosData,
                        TaxIncluded = entity.ProductInformation.TaxIncluded
                    },
                Customer = new CreateInvoiceRequest.BuyerInformation()
                {
                    BuyerAddress1 = entity.BuyerInformation.BuyerAddress1,
                    BuyerAddress2 = entity.BuyerInformation.BuyerAddress2,
                    BuyerCity = entity.BuyerInformation.BuyerCity,
                    BuyerCountry = entity.BuyerInformation.BuyerCountry,
                    BuyerEmail = entity.BuyerInformation.BuyerEmail,
                    BuyerName = entity.BuyerInformation.BuyerName,
                    BuyerPhone = entity.BuyerInformation.BuyerPhone,
                    BuyerState = entity.BuyerInformation.BuyerState,
                    BuyerZip = entity.BuyerInformation.BuyerZip
                },
                Checkout = new CreateInvoiceRequest.CheckoutOptions()
                {
                    ExpirationTime = entity.ExpirationTime,
                    PaymentTolerance = entity.PaymentTolerance,
                    PaymentMethods =
                        entity.GetPaymentMethods().Select(method => method.GetId().ToString()).ToArray(),
                    RedirectAutomatically = entity.RedirectAutomatically,
                    RedirectUri = entity.RedirectURL.ToString(),
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

        public Models.CreateInvoiceRequest FromModel(CreateInvoiceRequest entity)
        {
            return new Models.CreateInvoiceRequest()
            {
                Buyer = new Buyer()
                {
                    country = entity.Customer.BuyerCountry,
                    email = entity.Customer.BuyerEmail,
                    phone = entity.Customer.BuyerPhone,
                    zip = entity.Customer.BuyerZip,
                    Address1 = entity.Customer.BuyerAddress1,
                    Address2 = entity.Customer.BuyerAddress2,
                    City = entity.Customer.BuyerCity,
                    Name = entity.Customer.BuyerName,
                    State = entity.Customer.BuyerState,
                },
                Currency = entity.Currency,
                Physical = entity.Metadata.Physical,
                Price = entity.Amount,
                Refundable = true,
                ExtendedNotifications = true,
                FullNotifications = true,
                RedirectURL = entity.Checkout.RedirectUri,
                RedirectAutomatically = entity.Checkout.RedirectAutomatically,
                ItemCode = entity.Metadata.ItemCode,
                ItemDesc = entity.Metadata.ItemDesc,
                ExpirationTime = entity.Checkout.ExpirationTime,
                TransactionSpeed = entity.Checkout.SpeedPolicy?.ToString(),
                PaymentCurrencies = entity.Checkout.PaymentMethods,
                TaxIncluded = entity.Metadata.TaxIncluded,
                OrderId = entity.Metadata.OrderId,
                NotificationURL = entity.Checkout.RedirectUri,
                PosData = entity.Metadata.PosData
            };
        }
    }
}
