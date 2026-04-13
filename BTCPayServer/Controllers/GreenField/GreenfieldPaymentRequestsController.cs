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
using BTCPayServer.PaymentRequest;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
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
		private readonly LinkGenerator _linkGenerator;

		public GreenfieldPaymentRequestsController(
			InvoiceRepository invoiceRepository,
			UIInvoiceController invoiceController,
			PaymentRequestRepository paymentRequestRepository,
			PaymentRequestService paymentRequestService,
			CurrencyNameTable currencyNameTable,
			LinkGenerator linkGenerator)
		{
			_InvoiceRepository = invoiceRepository;
			_invoiceController = invoiceController;
			_paymentRequestRepository = paymentRequestRepository;
			PaymentRequestService = paymentRequestService;
			_currencyNameTable = currencyNameTable;
			_linkGenerator = linkGenerator;
		}

		[Authorize(Policy = Policies.CanViewPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
		[HttpGet("~/api/v1/stores/{storeId}/payment-requests")]
		public async Task<ActionResult<IEnumerable<Client.Models.PaymentRequestBaseData>>> GetPaymentRequests(string storeId, bool includeArchived = false)
		{
			var prs = await _paymentRequestRepository.FindPaymentRequests(
				new PaymentRequestQuery() { StoreId = storeId, IncludeArchived = includeArchived });
			return Ok(prs.Select(FromModel));
		}

		[Authorize(Policy = Policies.CanViewPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
		[HttpGet("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}")]
		[HttpGet("~/api/v1/payment-requests/{paymentRequestId}")]
		public async Task<IActionResult> GetPaymentRequest(string? storeId, string paymentRequestId)
		{
			var pr = HttpContext.GetPaymentRequestDataOrNull();

			if (pr is null)
				return PaymentRequestNotFound();

			return Ok(FromModel(pr));
		}

		[Authorize(Policy = Policies.CanViewPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
		[HttpPost("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}/pay")]
		[HttpPost("~/api/v1/payment-requests/{paymentRequestId}/pay")]
		public async Task<IActionResult> PayPaymentRequest(string? storeId, string paymentRequestId, [FromBody] PayPaymentRequestRequest pay, CancellationToken cancellationToken)
		{
			var p = HttpContext.GetPaymentRequestDataOrNull();
			if (p is null)
				return PaymentRequestNotFound();
            var pr = await PaymentRequestService.AsViewModel(p);
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
					return Ok(GreenfieldInvoiceController.ToModel(inv, _linkGenerator, _currencyNameTable, Request));
				}
			}

			try
			{
				var storeData = HttpContext.GetStoreData();
				var prData = await _paymentRequestRepository.FindPaymentRequest(pr.Id, null);
				var invoice = await _invoiceController.CreatePaymentRequestInvoice(prData, amount, pr.AmountDue, storeData, Request, cancellationToken);
				return Ok(GreenfieldInvoiceController.ToModel(invoice, _linkGenerator, _currencyNameTable, Request));
			}
			catch (BitpayHttpException e)
			{
				return this.CreateAPIError(null, e.Message);
			}
		}

		[Authorize(Policy = Policies.CanModifyPaymentRequests,
			AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
		[HttpDelete("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}")]
		[HttpDelete("~/api/v1/payment-requests/{paymentRequestId}")]
		public async Task<IActionResult> ArchivePaymentRequest(string? storeId, string paymentRequestId)
		{
			var pr = HttpContext.GetPaymentRequestDataOrNull();
			if (pr is null || pr.Archived)
				return PaymentRequestNotFound();

			await _paymentRequestRepository.ArchivePaymentRequest(pr.Id);
			return Ok();
		}

		[HttpPost("~/api/v1/stores/{storeId}/payment-requests")]
		[HttpPut("~/api/v1/stores/{storeId}/payment-requests/{paymentRequestId}")]
		[HttpPut("~/api/v1/payment-requests/{paymentRequestId}")]
		[Authorize(Policy = Policies.CanModifyPaymentRequests,
			AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
		public async Task<IActionResult> CreateOrUpdatePaymentRequest(
			[FromRoute] string? storeId,
			PaymentRequestBaseData request,
			[FromRoute] string? paymentRequestId = null)
		{
			if (request is null)
				return BadRequest();

            if (request.Amount <= 0)
            {
				ModelState.AddModelError(nameof(request.Amount), "Please provide an amount greater than 0");
            }
            if (!string.IsNullOrEmpty(request.Currency) &&
				_currencyNameTable.GetCurrencyData(request.Currency, false) == null)
				ModelState.AddModelError(nameof(request.Currency), "Invalid currency");
			if (string.IsNullOrEmpty(request.Currency))
				request.Currency = null;
			if (string.IsNullOrEmpty(request.Title))
				ModelState.AddModelError(nameof(request.Title), "Title is required");

			var storeData = HttpContext.GetStoreData();
			PaymentRequestData pr;
			if (paymentRequestId is not null)
			{
				pr = HttpContext.GetPaymentRequestDataOrNull();
				if (pr is null)
					return PaymentRequestNotFound();
				if ((pr.Amount != request.Amount && request.Amount != 0.0m) ||
					(pr.Currency != request.Currency && request.Currency != null))
				{
					var prWithInvoices = await this.PaymentRequestService.GetPaymentRequest(paymentRequestId, GetUserId());
					if (prWithInvoices.Invoices.Any())
					{
						ModelState.AddModelError(nameof(request.Amount), "Amount and currency are not editable once payment request has invoices");
					}
					else
					{
						if (request.Amount != 0.0m)
							pr.Amount = request.Amount;
						if (request.Currency != null)
							pr.Currency = request.Currency;
					}
				}
				pr.Expiry = request.ExpiryDate;
			}
			else
			{
				pr = new PaymentRequestData()
				{
					StoreDataId = storeId,
					Status = PaymentRequestStatus.Pending,
					Created = DateTimeOffset.UtcNow,
					Amount = request.Amount,
					Currency = request.Currency ?? storeData.GetStoreBlob().DefaultCurrency,
					Expiry = request.ExpiryDate,
				};
			}

			pr.ReferenceId = string.IsNullOrEmpty(request.ReferenceId) ? null : request.ReferenceId;

			if (!ModelState.IsValid)
				return this.CreateValidationError(ModelState);

			pr.Title = request.Title;
			var blob = pr.GetBlob();
			pr.SetBlob(new()
			{
				AllowCustomPaymentAmounts = request.AllowCustomPaymentAmounts,
				Description = request.Description,
				Email = request.Email,
				FormId = request.FormId,
				FormResponse = blob.FormId != request.FormId ? null : blob.FormResponse,
                RequestBaseUrl = Request.GetRequestBaseUrl().ToString()
			});
			pr = await _paymentRequestRepository.CreateOrUpdatePaymentRequest(pr);
			return Ok(FromModel(pr));
		}

		public PaymentRequestService PaymentRequestService { get; }

		private string GetUserId() => User.GetIdOrNull();

		private static Client.Models.PaymentRequestBaseData FromModel(PaymentRequestData data)
		{
			var blob = data.GetBlob();
			return new Client.Models.PaymentRequestBaseData()
			{
				CreatedTime = data.Created,
				Id = data.Id,
				StoreId = data.StoreDataId,
				Status = data.Status,
				Archived = data.Archived,
				Amount = data.Amount,
				Currency = data.Currency,
				Description = blob.Description,
				Title = data.Title,
				ExpiryDate = data.Expiry,
				Email = blob.Email,
				ReferenceId = data.ReferenceId,
				AllowCustomPaymentAmounts = blob.AllowCustomPaymentAmounts,
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
