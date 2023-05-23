using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Forms;
using BTCPayServer.Forms.Models;
using BTCPayServer.Models;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using PaymentRequestData = BTCPayServer.Data.PaymentRequestData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Route("payment-requests")]
    public class UIPaymentRequestController : Controller
    {
        private readonly UIInvoiceController _InvoiceController;
        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly PaymentRequestRepository _PaymentRequestRepository;
        private readonly PaymentRequestService _PaymentRequestService;
        private readonly EventAggregator _EventAggregator;
        private readonly CurrencyNameTable _Currencies;
        private readonly DisplayFormatter _displayFormatter;
        private readonly InvoiceRepository _InvoiceRepository;
        private readonly StoreRepository _storeRepository;

        private FormComponentProviders FormProviders { get; }
        public FormDataService FormDataService { get; }

        public UIPaymentRequestController(
            UIInvoiceController invoiceController,
            UserManager<ApplicationUser> userManager,
            PaymentRequestRepository paymentRequestRepository,
            PaymentRequestService paymentRequestService,
            EventAggregator eventAggregator,
            CurrencyNameTable currencies,
            DisplayFormatter displayFormatter,
            StoreRepository storeRepository,
            InvoiceRepository invoiceRepository,
            FormComponentProviders formProviders,
            FormDataService formDataService)
        {
            _InvoiceController = invoiceController;
            _UserManager = userManager;
            _PaymentRequestRepository = paymentRequestRepository;
            _PaymentRequestService = paymentRequestService;
            _EventAggregator = eventAggregator;
            _Currencies = currencies;
            _displayFormatter = displayFormatter;
            _storeRepository = storeRepository;
            _InvoiceRepository = invoiceRepository;
            FormProviders = formProviders;
            FormDataService = formDataService;
        }

        [BitpayAPIConstraint(false)]
        [HttpGet("/stores/{storeId}/payment-requests")]
        public async Task<IActionResult> GetPaymentRequests(string storeId, ListPaymentRequestsViewModel model = null)
        {
            model = this.ParseListQuery(model ?? new ListPaymentRequestsViewModel());

            var store = GetCurrentStore();
            var includeArchived = new SearchString(model.SearchTerm, model.TimezoneOffset ?? 0).GetFilterBool("includearchived") == true;
            var result = await _PaymentRequestRepository.FindPaymentRequests(new PaymentRequestQuery
            {
                UserId = GetUserId(),
                StoreId = store.Id,
                Skip = model.Skip,
                Count = model.Count,
                IncludeArchived = includeArchived
            });

            model.Items = result.Select(data =>
            {
                var blob = data.GetBlob();
                return new ViewPaymentRequestViewModel(data)
                {
                    AmountFormatted = _displayFormatter.Currency(blob.Amount, blob.Currency)
                };
            }).ToList();

            return View(model);
        }

        [HttpGet("/stores/{storeId}/payment-requests/edit/{payReqId?}")]
        public async Task<IActionResult> EditPaymentRequest(string storeId, string payReqId)
        {
            var store = GetCurrentStore();
            var paymentRequest = GetCurrentPaymentRequest();
            if (paymentRequest == null && !string.IsNullOrEmpty(payReqId))
            {
                return NotFound();
            }

            var prInvoices = payReqId is null ? null : (await _PaymentRequestService.GetPaymentRequest(payReqId, GetUserId())).Invoices;
            var vm = new UpdatePaymentRequestViewModel(paymentRequest)
            {
                StoreId = store.Id,
                AmountAndCurrencyEditable = payReqId is null || !prInvoices.Any()
            };

            vm.Currency ??= store.GetStoreBlob().DefaultCurrency;

            return View(nameof(EditPaymentRequest), vm);
        }

        [HttpPost("/stores/{storeId}/payment-requests/edit/{payReqId?}")]
        public async Task<IActionResult> EditPaymentRequest(string payReqId, UpdatePaymentRequestViewModel viewModel)
        {
            if (!string.IsNullOrEmpty(viewModel.Currency) &&
                _Currencies.GetCurrencyData(viewModel.Currency, false) == null)
                ModelState.AddModelError(nameof(viewModel.Currency), "Invalid currency");
            if (string.IsNullOrEmpty(viewModel.Currency))
                viewModel.Currency = null;
            var store = GetCurrentStore();
            var paymentRequest = GetCurrentPaymentRequest();
            if (paymentRequest == null && !string.IsNullOrEmpty(payReqId))
            {
                return NotFound();
            }

            if (paymentRequest?.Archived is true && viewModel.Archived)
            {
                ModelState.AddModelError(string.Empty, "You cannot edit an archived payment request.");
            }
            var data = paymentRequest ?? new PaymentRequestData();
            data.StoreDataId = viewModel.StoreId;
            data.Archived = viewModel.Archived;
            var blob = data.GetBlob();

            if (blob.Amount != viewModel.Amount && payReqId != null)
            {
                var prInvoices = (await _PaymentRequestService.GetPaymentRequest(payReqId, GetUserId())).Invoices;
                if (prInvoices.Any())
                    ModelState.AddModelError(nameof(viewModel.Amount), "Amount and currency are not editable once payment request has invoices");
            }

            if (!ModelState.IsValid)
            {
                return View(nameof(EditPaymentRequest), viewModel);
            }


            blob.Title = viewModel.Title;
            blob.Email = viewModel.Email;
            blob.Description = viewModel.Description;
            blob.Amount = viewModel.Amount;
            blob.ExpiryDate = viewModel.ExpiryDate?.ToUniversalTime();
            blob.Currency = viewModel.Currency ?? store.GetStoreBlob().DefaultCurrency;
            blob.EmbeddedCSS = viewModel.EmbeddedCSS;
            blob.CustomCSSLink = viewModel.CustomCSSLink;
            blob.AllowCustomPaymentAmounts = viewModel.AllowCustomPaymentAmounts;
            blob.FormId = viewModel.FormId;

            data.SetBlob(blob);
            var isNewPaymentRequest = string.IsNullOrEmpty(payReqId);
            if (isNewPaymentRequest)
            {
                data.Created = DateTimeOffset.UtcNow;
            }

            data = await _PaymentRequestRepository.CreateOrUpdatePaymentRequest(data);
            _EventAggregator.Publish(new PaymentRequestUpdated { Data = data, PaymentRequestId = data.Id, });

            TempData[WellKnownTempData.SuccessMessage] = $"Payment request \"{viewModel.Title}\" {(isNewPaymentRequest ? "created" : "updated")} successfully";
            return RedirectToAction(nameof(GetPaymentRequests), new { storeId = store.Id, payReqId = data.Id });
        }

        [HttpGet("{payReqId}")]
        [AllowAnonymous]
        public async Task<IActionResult> ViewPaymentRequest(string payReqId)
        {
            var vm = await _PaymentRequestService.GetPaymentRequest(payReqId, GetUserId());
            if (vm == null)
            {
                return NotFound();
            }
            var store = await _storeRepository.FindStore(vm.StoreId);
            if (store == null)
            {
                return NotFound();
            }

            var storeBlob = store.GetStoreBlob();
            vm.StoreName = store.StoreName;
            vm.BrandColor = storeBlob.BrandColor;
            vm.LogoFileId = storeBlob.LogoFileId;
            vm.CssFileId = storeBlob.CssFileId;
            vm.HubPath = PaymentRequestHub.GetHubPath(Request);

            return View(vm);
        }

        [HttpGet("{payReqId}/form")]
        [HttpPost("{payReqId}/form")]
        [AllowAnonymous]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> ViewPaymentRequestForm(string payReqId, FormViewModel viewModel)
        {
            var result = await _PaymentRequestRepository.FindPaymentRequest(payReqId, GetUserId());
            if (result == null)
            {
                return NotFound();
            }

            var prBlob = result.GetBlob();
            if (prBlob.FormResponse is not null)
            {
                return RedirectToAction("PayPaymentRequest", new { payReqId });
            }
            var prFormId = prBlob.FormId;
            var formData = await FormDataService.GetForm(prFormId);
            if (formData is null)
            {

                return RedirectToAction("PayPaymentRequest", new { payReqId });
            }

            var form = Form.Parse(formData.Config);
            if (!string.IsNullOrEmpty(prBlob.Email))
            {
                var emailField = form.GetFieldByFullName("buyerEmail");
                if (emailField is not null)
                {
                    emailField.Value = prBlob.Email;
                }
            }
            if (Request.Method == "POST" && Request.HasFormContentType)
            {
                form.ApplyValuesFromForm(Request.Form);
                if (FormDataService.Validate(form, ModelState))
                {
                    prBlob.FormResponse = FormDataService.GetValues(form);
                    result.SetBlob(prBlob);
                    await _PaymentRequestRepository.CreateOrUpdatePaymentRequest(result);
                    return RedirectToAction("PayPaymentRequest", new { payReqId });
                }
            }
            viewModel.FormName = formData.Name;
            viewModel.Form = form;
            return View("Views/UIForms/View", viewModel);

        }

        [HttpGet("{payReqId}/pay")]
        [AllowAnonymous]
        public async Task<IActionResult> PayPaymentRequest(string payReqId, bool redirectToInvoice = true,
            decimal? amount = null, CancellationToken cancellationToken = default)
        {
            if (amount.HasValue && amount.Value <= 0)
            {
                return BadRequest("Please provide an amount greater than 0");
            }

            var result = await _PaymentRequestService.GetPaymentRequest(payReqId, GetUserId());
            if (result == null)
            {
                return NotFound();
            }

            if (result.Archived)
            {
                if (redirectToInvoice)
                {
                    return RedirectToAction("ViewPaymentRequest", new { payReqId });
                }

                return BadRequest("Payment Request cannot be paid as it has been archived");
            }
            if (!result.FormSubmitted && !string.IsNullOrEmpty(result.FormId))
            {
                var formData = await FormDataService.GetForm(result.FormId);
                if (formData is not null)
                {
                    return RedirectToAction("ViewPaymentRequestForm", new { payReqId });
                }
            }

            result.HubPath = PaymentRequestHub.GetHubPath(Request);
            if (result.AmountDue <= 0)
            {
                if (redirectToInvoice)
                {
                    return RedirectToAction("ViewPaymentRequest", new { payReqId });
                }

                return BadRequest("Payment Request has already been settled.");
            }

            if (result.ExpiryDate.HasValue && DateTime.UtcNow >= result.ExpiryDate)
            {
                if (redirectToInvoice)
                {
                    return RedirectToAction("ViewPaymentRequest", new { payReqId });
                }

                return BadRequest("Payment Request has expired");
            }

            var currentInvoice = result.Invoices.GetReusableInvoice(amount);
            if (currentInvoice != null)
            {
                if (redirectToInvoice)
                {
                    return RedirectToAction("Checkout", "UIInvoice", new { currentInvoice.Id });
                }

                return Ok(currentInvoice.Id);
            }

            try
            {
                var store = await _storeRepository.FindStore(result.StoreId);
                var prData = await _PaymentRequestRepository.FindPaymentRequest(result.Id, null);
                var newInvoice = await _InvoiceController.CreatePaymentRequestInvoice(prData, amount, result.AmountDue, store, Request, cancellationToken);
                if (redirectToInvoice)
                {
                    return RedirectToAction("Checkout", "UIInvoice", new { invoiceId = newInvoice.Id });
                }

                return Ok(newInvoice.Id);
            }
            catch (BitpayHttpException e)
            {
                return BadRequest(e.Message);
            }
        }

        [HttpGet("{payReqId}/cancel")]
        public async Task<IActionResult> CancelUnpaidPendingInvoice(string payReqId, bool redirect = true)
        {
            var result = await _PaymentRequestService.GetPaymentRequest(payReqId, GetUserId());
            if (result == null)
            {
                return NotFound();
            }

            if (!result.AllowCustomPaymentAmounts)
            {
                return BadRequest("Not allowed to cancel this invoice");
            }

            var invoices = result.Invoices.Where(requestInvoice =>
                requestInvoice.State.Status == InvoiceStatusLegacy.New && !requestInvoice.Payments.Any());

            if (!invoices.Any())
            {
                return BadRequest("No unpaid pending invoice to cancel");
            }

            foreach (var invoice in invoices)
            {
                await _InvoiceRepository.MarkInvoiceStatus(invoice.Id, InvoiceStatus.Invalid);
            }

            if (redirect)
            {
                TempData[WellKnownTempData.SuccessMessage] = "Payment cancelled";
                return RedirectToAction(nameof(ViewPaymentRequest), new { payReqId });
            }

            return Ok("Payment cancelled");
        }

        [HttpGet("{payReqId}/clone")]
        public async Task<IActionResult> ClonePaymentRequest(string payReqId)
        {
            var store = GetCurrentStore();
            var result = await EditPaymentRequest(store.Id, payReqId);
            if (result is ViewResult viewResult)
            {
                var model = (UpdatePaymentRequestViewModel)viewResult.Model;
                model.Id = null;
                model.Archived = false;
                model.ExpiryDate = null;
                model.Title = $"Clone of {model.Title}";
                model.AmountAndCurrencyEditable = true;
                return View("EditPaymentRequest", model);
            }

            return NotFound();
        }

        [HttpGet("{payReqId}/archive")]
        public async Task<IActionResult> TogglePaymentRequestArchival(string payReqId)
        {
            var store = GetCurrentStore();
            var result = await EditPaymentRequest(store.Id, payReqId);
            if (result is ViewResult viewResult)
            {
                var model = (UpdatePaymentRequestViewModel)viewResult.Model;
                model.Archived = !model.Archived;
                await EditPaymentRequest(payReqId, model);
                TempData[WellKnownTempData.SuccessMessage] = model.Archived
                    ? "The payment request has been archived and will no longer appear in the payment request list by default again."
                    : "The payment request has been unarchived and will appear in the payment request list by default.";
                return RedirectToAction("GetPaymentRequests", new { storeId = store.Id });
            }

            return NotFound();
        }

        private string GetUserId() => _UserManager.GetUserId(User);

        private StoreData GetCurrentStore() => HttpContext.GetStoreData();

        private PaymentRequestData GetCurrentPaymentRequest() => HttpContext.GetPaymentRequestData();
    }
}
