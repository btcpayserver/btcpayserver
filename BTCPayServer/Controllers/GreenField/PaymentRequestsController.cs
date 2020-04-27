using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PaymentRequestData = BTCPayServer.Data.PaymentRequestData;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class PaymentRequestsController : ControllerBase
    {
        private readonly PaymentRequestRepository _paymentRequestRepository;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CurrencyNameTable _currencyNameTable;

        public PaymentRequestsController(PaymentRequestRepository paymentRequestRepository,
            UserManager<ApplicationUser> userManager, CurrencyNameTable currencyNameTable)
        {
            _paymentRequestRepository = paymentRequestRepository;
            _userManager = userManager;
            _currencyNameTable = currencyNameTable;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-requests")]
        public async Task<ActionResult<IEnumerable<PaymentRequestData>>> GetPaymentRequests(string storeId)
        {
            var prs = await _paymentRequestRepository.FindPaymentRequests(
                new PaymentRequestQuery() {StoreId = storeId});
            return Ok(prs.Items.Select(FromModel));
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}")]
        public async Task<ActionResult<PaymentRequestData>> GetPaymentRequest(string storeId, string paymentRequestId)
        {
            var pr = await _paymentRequestRepository.FindPaymentRequests(
                new PaymentRequestQuery() {StoreId = storeId, Ids = new[] {paymentRequestId}});

            if (pr.Total == 0)
            {
                return NotFound();
            }

            return Ok(FromModel(pr.Items.First()));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}")]
        public async Task<ActionResult> RemovePaymentRequest(string storeId, string paymentRequestId)
        {
            var pr = await _paymentRequestRepository.FindPaymentRequests(
                new PaymentRequestQuery() {StoreId = storeId, Ids = new[] {paymentRequestId}});
            if (pr.Total == 0)
            {
                return NotFound();
            }

            var result =
                await _paymentRequestRepository.RemovePaymentRequest(paymentRequestId, _userManager.GetUserId(User));
            return result ? (ActionResult)Ok() : BadRequest();
        }

        [HttpPost("~/api/v1/stores/{storeId}/payment-requests")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> CreatePaymentRequest(string storeId,
            CreatePaymentRequestRequest request)
        {
            var validationResult = Validate(request);
            if (validationResult != null)
            {
                return validationResult;
            }

            var pr = new PaymentRequestData()
            {
                StoreDataId = storeId,
                Status = Client.Models.PaymentRequestData.PaymentRequestStatus.Pending,
                Created = DateTimeOffset.Now
            };
            pr.SetBlob(request);
            pr = await _paymentRequestRepository.CreateOrUpdatePaymentRequest(pr);
            return Ok(FromModel(pr));
        }

        [HttpPut("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> UpdatePaymentRequest(string storeId,
            string paymentRequestId, [FromBody] UpdatePaymentRequestRequest request)
        {
            var validationResult = Validate(request);
            if (validationResult != null)
            {
                return validationResult;
            }

            var pr = await _paymentRequestRepository.FindPaymentRequests(
                new PaymentRequestQuery() {StoreId = storeId, Ids = new[] {paymentRequestId}});
            if (pr.Total == 0)
            {
                return NotFound();
            }

            var updatedPr = pr.Items.First();
            updatedPr.SetBlob(request);

            return Ok(FromModel(await _paymentRequestRepository.CreateOrUpdatePaymentRequest(updatedPr)));
        }

        private IActionResult Validate(PaymentRequestBaseData data)
        {
            if (data is null)
                return BadRequest();
            if (data.Amount <= 0)
            {
                ModelState.AddModelError(nameof(data.Amount), "Please provide an amount greater than 0");
            }

            if (string.IsNullOrEmpty(data.Currency) ||
                _currencyNameTable.GetCurrencyData(data.Currency, false) == null)
                ModelState.AddModelError(nameof(data.Currency), "Invalid currency");
            
            if (string.IsNullOrEmpty(data.Title))
                ModelState.AddModelError(nameof(data.Title), "Title is required"); 
            
            if (!string.IsNullOrEmpty(data.CustomCSSLink) && data.CustomCSSLink.Length >500)
                ModelState.AddModelError(nameof(data.CustomCSSLink), "CustomCSSLink is 500 chars max");

            return !ModelState.IsValid ? BadRequest(new ValidationProblemDetails(ModelState)) : null;
        }

        private static Client.Models.PaymentRequestData FromModel(PaymentRequestData data)
        {
            var blob = data.GetBlob();
            return new Client.Models.PaymentRequestData()
            {
                Created = data.Created,
                Id = data.Id,
                Status = data.Status,
                Amount = blob.Amount,
                Currency = blob.Currency,
                Description = blob.Description,
                Title = blob.Title,
                ExpiryDate = blob.ExpiryDate,
                Email = blob.Email,
                AllowCustomPaymentAmounts = blob.AllowCustomPaymentAmounts,
                EmbeddedCSS = blob.EmbeddedCSS,
                CustomCSSLink = blob.CustomCSSLink
            };
        }
    }
}
