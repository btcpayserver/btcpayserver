using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Ganss.XSS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using NBitpayClient;

namespace BTCPayServer.Controllers
{
    [Route("payment-requests")]
    [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
    public class PaymentRequestController : Controller
    {
        private readonly InvoiceController _InvoiceController;
        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly StoreRepository _StoreRepository;
        private readonly PaymentRequestRepository _PaymentRequestRepository;
        private readonly PaymentRequestService _PaymentRequestService;
        private readonly EventAggregator _EventAggregator;
        private readonly CurrencyNameTable _Currencies;
        private readonly HtmlSanitizer _htmlSanitizer;

        public PaymentRequestController(
            InvoiceController invoiceController,
            UserManager<ApplicationUser> userManager,
            StoreRepository storeRepository,
            PaymentRequestRepository paymentRequestRepository,
            PaymentRequestService paymentRequestService,
            EventAggregator eventAggregator,
            CurrencyNameTable currencies,
            HtmlSanitizer htmlSanitizer)
        {
            _InvoiceController = invoiceController;
            _UserManager = userManager;
            _StoreRepository = storeRepository;
            _PaymentRequestRepository = paymentRequestRepository;
            _PaymentRequestService = paymentRequestService;
            _EventAggregator = eventAggregator;
            _Currencies = currencies;
            _htmlSanitizer = htmlSanitizer;
        }

        [HttpGet]
        [Route("")]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> GetPaymentRequests(int skip = 0, int count = 50, string statusMessage = null)
        {
            var result = await _PaymentRequestRepository.FindPaymentRequests(new PaymentRequestQuery()
            {
                UserId = GetUserId(), Skip = skip, Count = count
            });
            return View(new ListPaymentRequestsViewModel()
            {
                Skip = skip,
                StatusMessage = statusMessage,
                Count = count,
                Total = result.Total,
                Items = result.Items.Select(data => new ViewPaymentRequestViewModel(data)).ToList()
            });
        }

        [HttpGet]
        [Route("edit/{id?}")]
        public async Task<IActionResult> EditPaymentRequest(string id, string statusMessage = null)
        {
            SelectList stores = null;
            var data = await _PaymentRequestRepository.FindPaymentRequest(id, GetUserId());
            if (data == null && !string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            stores = new SelectList(await _StoreRepository.GetStoresByUserId(GetUserId()), nameof(StoreData.Id),
                nameof(StoreData.StoreName), data?.StoreDataId);
            if (!stores.Any())
            {
                return RedirectToAction("GetPaymentRequests",
                    new
                    {
                        StatusMessage = new StatusMessageModel()
                        {
                            Html =
                                $"Error: You need to create at least one store. <a href='{Url.Action("CreateStore", "UserStores")}'>Create store</a>",
                            Severity = StatusMessageModel.StatusSeverity.Error
                        }
                    });
            }

            return View(new UpdatePaymentRequestViewModel(data)
            {
                Stores = stores,
                StatusMessage = statusMessage
            });
        }

        [HttpPost]
        [Route("edit/{id?}")]
        public async Task<IActionResult> EditPaymentRequest(string id, UpdatePaymentRequestViewModel viewModel)
        {
            if (string.IsNullOrEmpty(viewModel.Currency) ||
                _Currencies.GetCurrencyData(viewModel.Currency, false) == null)
                ModelState.AddModelError(nameof(viewModel.Currency), "Invalid currency");

            var data = await _PaymentRequestRepository.FindPaymentRequest(id, GetUserId());
            if (data == null && !string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                viewModel.Stores = new SelectList(await _StoreRepository.GetStoresByUserId(GetUserId()),
                    nameof(StoreData.Id),
                    nameof(StoreData.StoreName), data?.StoreDataId);

                return View(viewModel);
            }

            if (data == null)
            {
                data = new PaymentRequestData();
            }

            data.StoreDataId = viewModel.StoreId;
            var blob = data.GetBlob();

            blob.Title = viewModel.Title;
            blob.Email = viewModel.Email;
            blob.Description = _htmlSanitizer.Sanitize(viewModel.Description);
            blob.Amount = viewModel.Amount;
            blob.ExpiryDate = viewModel.ExpiryDate;
            blob.Currency = viewModel.Currency;
            blob.EmbeddedCSS = viewModel.EmbeddedCSS;
            blob.CustomCSSLink = viewModel.CustomCSSLink;
            blob.AllowCustomPaymentAmounts = viewModel.AllowCustomPaymentAmounts;

            data.SetBlob(blob);
            data = await _PaymentRequestRepository.CreateOrUpdatePaymentRequest(data);
            _EventAggregator.Publish(new PaymentRequestUpdated()
            {
                Data = data,
                PaymentRequestId = data.Id
            });

            return RedirectToAction("EditPaymentRequest", new {id = data.Id, StatusMessage = "Saved"});
        }

        [HttpGet]
        [Route("{id}/remove")]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> RemovePaymentRequestPrompt(string id)
        {
            var data = await _PaymentRequestRepository.FindPaymentRequest(id, GetUserId());
            if (data == null)
            {
                return NotFound();
            }

            var blob = data.GetBlob();
            return View("Confirm", new ConfirmModel()
            {
                Title = $"Remove Payment Request",
                Description = $"Are you sure you want to remove access to the payment request '{blob.Title}' ?",
                Action = "Delete"
            });
        }

        [HttpPost]
        [Route("{id}/remove")]
        [BitpayAPIConstraint(false)]
        public async Task<IActionResult> RemovePaymentRequest(string id)
        {
            var result = await _PaymentRequestRepository.RemovePaymentRequest(id, GetUserId());
            if (result)
            {
                return RedirectToAction("GetPaymentRequests",
                    new {StatusMessage = "Payment request successfully removed"});
            }
            else
            {
                return RedirectToAction("GetPaymentRequests",
                    new
                    {
                        StatusMessage =
                            "Error: Payment request could not be removed. Any request that has generated invoices cannot be removed."
                    });
            }
        }

        [HttpGet]
        [Route("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> ViewPaymentRequest(string id)
        {
            var result = await _PaymentRequestService.GetPaymentRequest(id, GetUserId());
            if (result == null)
            {
                return NotFound();
            }
            result.HubPath = PaymentRequestHub.GetHubPath(this.Request);
            return View(result);
        }

        [HttpGet]
        [Route("{id}/pay")]
        [AllowAnonymous]
        public async Task<IActionResult> PayPaymentRequest(string id, bool redirectToInvoice = true,
            decimal? amount = null, CancellationToken cancellationToken = default)
        {
            var result = await _PaymentRequestService.GetPaymentRequest(id, GetUserId());
            if (result == null)
            {
                return NotFound();
            }
            result.HubPath = PaymentRequestHub.GetHubPath(this.Request);
            if (result.AmountDue <= 0)
            {
                if (redirectToInvoice)
                {
                    return RedirectToAction("ViewPaymentRequest", new {Id = id});
                }

                return BadRequest("Payment Request has already been settled.");
            }

            if (result.ExpiryDate.HasValue && DateTime.Now >= result.ExpiryDate)
            {
                if (redirectToInvoice)
                {
                    return RedirectToAction("ViewPaymentRequest", new {Id = id});
                }

                return BadRequest("Payment Request has expired");
            }

            var statusesAllowedToDisplay = new List<InvoiceStatus>()
            {
                InvoiceStatus.New
            };
            var validInvoice = result.Invoices.FirstOrDefault(invoice =>
                Enum.TryParse<InvoiceStatus>(invoice.Status, true, out var status) &&
                statusesAllowedToDisplay.Contains(status));

            if (validInvoice != null)
            {
                if (redirectToInvoice)
                {
                    return RedirectToAction("Checkout", "Invoice", new {Id = validInvoice.Id});
                }

                return Ok(validInvoice.Id);
            }

            if (result.AllowCustomPaymentAmounts && amount != null)
                amount = Math.Min(result.AmountDue, amount.Value);
            else
                amount = result.AmountDue;


            var pr = await _PaymentRequestRepository.FindPaymentRequest(id, null);
            var blob = pr.GetBlob();
            var store = pr.StoreData;
            store.AdditionalClaims.Add(new Claim(Policies.CanCreateInvoice.Key, store.Id));
            try
            {
                var redirectUrl = Request.GetDisplayUrl().TrimEnd("/pay", StringComparison.InvariantCulture)
                    .Replace("hub?id=", string.Empty, StringComparison.InvariantCultureIgnoreCase);
                var newInvoiceId = (await _InvoiceController.CreateInvoiceCore(new CreateInvoiceRequest()
                        {
                            OrderId = $"{PaymentRequestRepository.GetOrderIdForPaymentRequest(id)}",
                            Currency = blob.Currency,
                            Price = amount.Value,
                            FullNotifications = true,
                            BuyerEmail = result.Email,
                            RedirectURL = redirectUrl,
                        }, store, HttpContext.Request.GetAbsoluteRoot(),
                        new List<string>() {PaymentRequestRepository.GetInternalTag(id)},
                        cancellationToken: cancellationToken))
                    .Data.Id;

                if (redirectToInvoice)
                {
                    return RedirectToAction("Checkout", "Invoice", new {Id = newInvoiceId});
                }

                return Ok(newInvoiceId);
            }
            catch (BitpayHttpException e)
            {
                return BadRequest(e.Message);
            }
        }


        private string GetUserId()
        {
            return _UserManager.GetUserId(User);
        }

        [HttpGet]
        [Route("{id}/clone")]
        public async Task<IActionResult> ClonePaymentRequest(string id)
        {
            var result = await EditPaymentRequest(id);
            if (result is ViewResult viewResult)
            {
                var model = (UpdatePaymentRequestViewModel)viewResult.Model;
                model.Id = null;
                model.Title = $"Clone of {model.Title}";
                
                return View("EditPaymentRequest", model);
                
            }

            return NotFound();
        }
    }
}
