using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PaymentRequestData = BTCPayServer.Data.PaymentRequestData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldPaymentRequestsController : ControllerBase
    {
        private readonly InvoiceRepository _InvoiceRepository;
        private readonly UIInvoiceController _invoiceController;
        private readonly PaymentRequestRepository _paymentRequestRepository;
        private readonly CurrencyNameTable _currencyNameTable;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly LinkGenerator _linkGenerator;

        public GreenfieldPaymentRequestsController(
            InvoiceRepository invoiceRepository,
            UIInvoiceController invoiceController,
            PaymentRequestRepository paymentRequestRepository,
            PaymentRequestService paymentRequestService,
            CurrencyNameTable currencyNameTable,
            UserManager<ApplicationUser> userManager,
            LinkGenerator linkGenerator)
        {
            _InvoiceRepository = invoiceRepository;
            _invoiceController = invoiceController;
            _paymentRequestRepository = paymentRequestRepository;
            PaymentRequestService = paymentRequestService;
            _currencyNameTable = currencyNameTable;
            _userManager = userManager;
            _linkGenerator = linkGenerator;
        }

        [Authorize(Policy = Policies.CanViewPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-requests")]
        public async Task<ActionResult<IEnumerable<Client.Models.PaymentRequestData>>> GetPaymentRequests(string storeId, bool includeArchived = false)
        {
            var prs = await _paymentRequestRepository.FindPaymentRequests(
                new PaymentRequestQuery() { StoreId = storeId, IncludeArchived = includeArchived });
            return Ok(prs.Select(FromModel));
        }

        [Authorize(Policy = Policies.CanViewPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}")]
        public async Task<IActionResult> GetPaymentRequest(string storeId, string paymentRequestId)
        {
            var pr = await _paymentRequestRepository.FindPaymentRequests(
                new PaymentRequestQuery() { StoreId = storeId, Ids = new[] { paymentRequestId } });

            if (pr.Length == 0)
            {
                return PaymentRequestNotFound();
            }

            return Ok(FromModel(pr.First()));
        }

        [Authorize(Policy = Policies.CanViewPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}/pay")]
        public async Task<IActionResult> PayPaymentRequest(string storeId, string paymentRequestId, [FromBody] PayPaymentRequestRequest pay, CancellationToken cancellationToken)
        {
            var pr = await this.PaymentRequestService.GetPaymentRequest(paymentRequestId);
            if (pr is null || pr.StoreId != storeId)
                return PaymentRequestNotFound();

            var amount = pay?.Amount;
            if (amount.HasValue && amount.Value <= 0)
            {
                ModelState.AddModelError(nameof(pay.Amount), "The amount should be more than 0");
            }
            if (amount.HasValue && !pr.AllowCustomPaymentAmounts && amount.Value != pr.AmountDue)
            {
                ModelState.AddModelError(nameof(pay.Amount), "This payment request doesn't allow custom payment amount");
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            if (pr.Archived)
            {
                return this.CreateAPIError("archived", "You cannot pay an archived payment request");
            }

            if (pr.AmountDue <= 0)
            {
                return this.CreateAPIError("already-paid", "This payment request is already paid");
            }

            if (pr.ExpiryDate.HasValue && DateTime.UtcNow >= pr.ExpiryDate)
            {
                return this.CreateAPIError("expired", "This payment request is expired");
            }

            if (pay?.AllowPendingInvoiceReuse is true)
            {
                if (pr.Invoices.GetReusableInvoice(amount)?.Id is string invoiceId)
                {
                    var inv = await _InvoiceRepository.GetInvoice(invoiceId);
                    return Ok(GreenfieldInvoiceController.ToModel(inv, _linkGenerator, Request));
                }
            }

            try
            {
                var prData = await _paymentRequestRepository.FindPaymentRequest(pr.Id, null);
                var invoice = await _invoiceController.CreatePaymentRequestInvoice(prData, amount, pr.AmountDue, this.StoreData, Request, cancellationToken);
                return Ok(GreenfieldInvoiceController.ToModel(invoice, _linkGenerator, Request));
            }
            catch (BitpayHttpException e)
            {
                return this.CreateAPIError(null, e.Message);
            }
        }

        [Authorize(Policy = Policies.CanModifyPaymentRequests,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}")]
        public async Task<IActionResult> ArchivePaymentRequest(string storeId, string paymentRequestId)
        {
            var pr = await _paymentRequestRepository.FindPaymentRequests(
                new PaymentRequestQuery() { StoreId = storeId, Ids = new[] { paymentRequestId }, IncludeArchived = false });
            if (pr.Length == 0)
            {
                return PaymentRequestNotFound();
            }

            await _paymentRequestRepository.ArchivePaymentRequest(pr.First().Id);
            return Ok();
        }

        [HttpPost("~/api/v1/stores/{storeId}/payment-requests")]
        [Authorize(Policy = Policies.CanModifyPaymentRequests,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreatePaymentRequest(string storeId,
            CreatePaymentRequestRequest request)
        {
            var validationResult = await Validate(null, request);
            if (validationResult != null)
            {
                return validationResult;
            }
            request.Currency ??= StoreData.GetStoreBlob().DefaultCurrency;
            var pr = new PaymentRequestData()
            {
                StoreDataId = storeId,
                Status = Client.Models.PaymentRequestData.PaymentRequestStatus.Pending,
                Created = DateTimeOffset.UtcNow
            };
            pr.SetBlob(request);
            pr = await _paymentRequestRepository.CreateOrUpdatePaymentRequest(pr);
            return Ok(FromModel(pr));
        }
        public Data.StoreData StoreData => HttpContext.GetStoreData();

        public PaymentRequestService PaymentRequestService { get; }

        [HttpPut("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}")]
        [Authorize(Policy = Policies.CanModifyPaymentRequests,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> UpdatePaymentRequest(string storeId,
            string paymentRequestId, [FromBody] UpdatePaymentRequestRequest request)
        {
            var validationResult = await Validate(paymentRequestId, request);
            if (validationResult != null)
            {
                return validationResult;
            }
            request.Currency ??= StoreData.GetStoreBlob().DefaultCurrency;
            var pr = await _paymentRequestRepository.FindPaymentRequests(
                new PaymentRequestQuery() { StoreId = storeId, Ids = new[] { paymentRequestId } });
            if (pr.Length == 0)
            {
                return PaymentRequestNotFound();
            }

            var updatedPr = pr.First();
            updatedPr.SetBlob(request);

            return Ok(FromModel(await _paymentRequestRepository.CreateOrUpdatePaymentRequest(updatedPr)));
        }
        private string GetUserId() => _userManager.GetUserId(User);

        private async Task<IActionResult> Validate(string id, PaymentRequestBaseData data)
        {
            if (data is null)
                return BadRequest();

            if (id != null)
            {
                var pr = await this.PaymentRequestService.GetPaymentRequest(id, GetUserId());
                if (pr.Amount != data.Amount)
                {
                    if (pr.Invoices.Any())
                        ModelState.AddModelError(nameof(data.Amount), "Amount and currency are not editable once payment request has invoices");
                }
            }
            if (data.Amount <= 0)
            {
                ModelState.AddModelError(nameof(data.Amount), "Please provide an amount greater than 0");
            }

            if (!string.IsNullOrEmpty(data.Currency) &&
                _currencyNameTable.GetCurrencyData(data.Currency, false) == null)
                ModelState.AddModelError(nameof(data.Currency), "Invalid currency");
            if (string.IsNullOrEmpty(data.Currency))
                data.Currency = null;
            if (string.IsNullOrEmpty(data.Title))
                ModelState.AddModelError(nameof(data.Title), "Title is required");

            if (!string.IsNullOrEmpty(data.CustomCSSLink) && data.CustomCSSLink.Length > 500)
                ModelState.AddModelError(nameof(data.CustomCSSLink), "CustomCSSLink is 500 chars max");

            return !ModelState.IsValid ? this.CreateValidationError(ModelState) : null;
        }

        private static Client.Models.PaymentRequestData FromModel(PaymentRequestData data)
        {
            var blob = data.GetBlob();
            return new Client.Models.PaymentRequestData()
            {
                CreatedTime = data.Created,
                Id = data.Id,
                StoreId = data.StoreDataId,
                Status = data.Status,
                Archived = data.Archived,
                Amount = blob.Amount,
                Currency = blob.Currency,
                Description = blob.Description,
                Title = blob.Title,
                ExpiryDate = blob.ExpiryDate,
                Email = blob.Email,
                AllowCustomPaymentAmounts = blob.AllowCustomPaymentAmounts,
                EmbeddedCSS = blob.EmbeddedCSS,
                CustomCSSLink = blob.CustomCSSLink,
                FormResponse = blob.FormResponse,
                FormId = blob.FormId
            };
        }

        private IActionResult PaymentRequestNotFound()
        {
            return this.CreateAPIError(404, "payment-request-not-found", "The payment request was not found");
        }
    }
}
