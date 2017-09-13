using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Invoicing;
using BTCPayServer.Models.InvoicingModels;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController
    {

		[HttpGet]
		[Route("i/{invoiceId}")]
		[AcceptMediaTypeConstraint("application/bitcoin-paymentrequest", false)]
		public async Task<IActionResult> Checkout(string invoiceId)
		{
			var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
			if(invoice == null)
				return NotFound();
			var store = await _StoreRepository.FindStore(invoice.StoreId);
			var dto = EntityToDTO(invoice);

			var model = new PaymentModel()
			{
				OrderId = invoice.OrderId,
				InvoiceId = invoice.Id,
				BTCAddress = invoice.DepositAddress.ToString(),
				BTCAmount = (invoice.GetTotalCryptoDue() - invoice.TxFee).ToString(),
				BTCTotalDue = invoice.GetTotalCryptoDue().ToString(),
				BTCDue = invoice.GetCryptoDue().ToString(),
				CustomerEmail = invoice.RefundMail,
				ExpirationSeconds = Math.Max(0, (int)(invoice.ExpirationTime - DateTimeOffset.UtcNow).TotalSeconds),
				MaxTimeSeconds = (int)(invoice.ExpirationTime - invoice.InvoiceTime).TotalSeconds,
				ItemDesc = invoice.ProductInformation.ItemDesc,
				Rate = invoice.Rate.ToString(),
				RedirectUrl = invoice.RedirectURL,
				StoreName = store.StoreName,
				TxFees = invoice.TxFee.ToString(),
				InvoiceBitcoinUrl = dto.PaymentUrls.BIP72,
				TxCount = invoice.Payments.Count + 1,
				Status = invoice.Status
			};

			var expiration = TimeSpan.FromSeconds((double)model.ExpirationSeconds);
			model.TimeLeft = PrettyPrint(expiration);
			return View(model);
		}

		private string PrettyPrint(TimeSpan expiration)
		{
			StringBuilder builder = new StringBuilder();
			if(expiration.Days >= 1)
				builder.Append(expiration.Days.ToString());
			if(expiration.Hours >= 1)
				builder.Append(expiration.Hours.ToString("00"));
			builder.Append($"{expiration.Minutes.ToString("00")}:{expiration.Seconds.ToString("00")}");
			return builder.ToString();
		}

		[HttpGet]
		[Route("i/{invoiceId}/status")]
		public async Task<IActionResult> GetStatus(string invoiceId)
		{
			var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId);
			if(invoice == null)
				return NotFound();
			return Content(invoice.Status);
		}

		[HttpPost]
		[Route("i/{invoiceId}/UpdateCustomer")]
		public async Task<IActionResult> UpdateCustomer(string invoiceId, [FromBody]UpdateCustomerModel data)
		{
			if(!ModelState.IsValid)
			{
				return BadRequest(ModelState);
			}
			await _InvoiceRepository.UpdateInvoice(invoiceId, data).ConfigureAwait(false);
			return Ok();
		}

		[HttpGet]
		[Route("Invoices")]
		[BitpayAPIConstraint(false)]
		public async Task<IActionResult> ListInvoices(string searchTerm = null, int skip = 0, int count = 20)
		{
			var store = await FindStore(User);
			var model = new InvoicesModel();
			foreach(var invoice in await _InvoiceRepository.GetInvoices(new InvoiceQuery()
			{
				TextSearch = searchTerm,
				Count = count,
				Skip = skip,
				StoreId = store.Id
			}))
			{
				model.SearchTerm = searchTerm;
				model.Invoices.Add(new InvoiceModel()
				{
					Status = invoice.Status,
					Date = invoice.InvoiceTime,
					InvoiceId = invoice.Id,
					AmountCurrency = $"{invoice.ProductInformation.Price.ToString(CultureInfo.InvariantCulture)} {invoice.ProductInformation.Currency}"
				});
			}
			model.Skip = skip;
			model.Count = count;
			model.StatusMessage = StatusMessage;
			return View(model);
		}

		[HttpGet]
		[Route("Invoices/Create")]
		[BitpayAPIConstraint(false)]
		public IActionResult CreateInvoice()
		{
			return View();
		}

		[HttpPost]
		[Route("Invoices/Create")]
		[BitpayAPIConstraint(false)]
		public async Task<IActionResult> CreateInvoice(CreateInvoiceModel model)
		{
			if(!ModelState.IsValid)
			{
				return View(model);
			}
			var store = await FindStore(User);
			var result = await CreateInvoiceCore(new Invoice()
			{
				Price = model.Amount.Value,
				Currency = "USD",
				PosData = model.PosData,
				OrderId = model.OrderId,
				//RedirectURL = redirect + "redirect",
				//NotificationURL = CallbackUri + "/notification",
				ItemDesc = model.ItemDesc,
				FullNotifications = true,
				BuyerEmail = model.BuyerEmail
			}, store);
			
			StatusMessage = $"Invoice {result.Data.Id} just created!";
			return RedirectToAction("ListInvoices");
		}

		[HttpPost]
		[BitpayAPIConstraint(false)]
		public IActionResult SearchInvoice(InvoicesModel invoices)
		{
			return RedirectToAction("Index", new
			{
				searchTerm = invoices.SearchTerm,
				skip = invoices.Skip,
				count = invoices.Count,
			});
		}

		[TempData]
		public string StatusMessage
		{
			get;
			set;
		}

		private async Task<StoreData> FindStore(ClaimsPrincipal user)
		{
			var usr = await _UserManager.GetUserAsync(User);
			if(user == null)
			{
				throw new ApplicationException($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
			}
			return await _StoreRepository.GetStore(usr.Id);
		}
	}
}
