using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Forms;
using BTCPayServer.Forms.Models;
using BTCPayServer.Models;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PaymentRequestData = BTCPayServer.Data.PaymentRequestData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    [Route("payment-requests")]
    [Authorize(Policy = Policies.CanViewPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIPaymentRequestController : Controller
    {
        private readonly UIInvoiceController _InvoiceController;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly PaymentRequestRepository _PaymentRequestRepository;
        private readonly PaymentRequestService _PaymentRequestService;
        private readonly CurrencyNameTable _Currencies;
        private readonly DisplayFormatter _displayFormatter;
        private readonly InvoiceRepository _InvoiceRepository;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly StoreRepository _storeRepository;
        private readonly UriResolver _uriResolver;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly WalletRepository _walletRepository;

        private FormComponentProviders FormProviders { get; }
        public FormDataService FormDataService { get; }
        public IStringLocalizer StringLocalizer { get; }

        public UIPaymentRequestController(
            UIInvoiceController invoiceController,
            PaymentMethodHandlerDictionary handlers,
            UserManager<ApplicationUser> userManager,
            PaymentRequestRepository paymentRequestRepository,
            PaymentRequestService paymentRequestService,
            CurrencyNameTable currencies,
            DisplayFormatter displayFormatter,
            StoreRepository storeRepository,
            UriResolver uriResolver,
            InvoiceRepository invoiceRepository,
            FormComponentProviders formProviders,
            FormDataService formDataService,
            IStringLocalizer stringLocalizer,
            ApplicationDbContextFactory dbContextFactory,
            BTCPayNetworkProvider networkProvider,
            WalletRepository walletRepository)
        {
            _InvoiceController = invoiceController;
            _handlers = handlers;
            _UserManager = userManager;
            _PaymentRequestRepository = paymentRequestRepository;
            _PaymentRequestService = paymentRequestService;
            _Currencies = currencies;
            _displayFormatter = displayFormatter;
            _storeRepository = storeRepository;
            _uriResolver = uriResolver;
            _InvoiceRepository = invoiceRepository;
            _dbContextFactory = dbContextFactory;
            FormProviders = formProviders;
            FormDataService = formDataService;
            _networkProvider = networkProvider;
            StringLocalizer = stringLocalizer;
            _walletRepository = walletRepository;
        }

        [HttpGet("/stores/{storeId}/payment-requests")]
        [Authorize(Policy = Policies.CanViewPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> GetPaymentRequests(string storeId, ListPaymentRequestsViewModel model = null)
        {
            model = this.ParseListQuery(model ?? new ListPaymentRequestsViewModel());

            var store = GetCurrentStore();
            var defaultNetwork = _networkProvider.DefaultNetwork;
            var walletId = new WalletId(store.Id, defaultNetwork.CryptoCode);
            model.WalletId = walletId.ToString();

            var timezoneOffset = model.TimezoneOffset ?? 0;
            var fs = new SearchString(model.SearchTerm, timezoneOffset);
            var textSearch = model.SearchText;
            var startDate = fs.GetFilterDate("startdate", timezoneOffset);
            var endDate   = fs.GetFilterDate("enddate",   timezoneOffset);

            var result = await _PaymentRequestRepository.FindPaymentRequests(new PaymentRequestQuery
            {
                UserId = GetUserId(),
                StoreId = store.Id,
                WalletId = model.WalletId,
                Skip = model.Skip,
                Count = model.Count,
                Status = fs.GetFilterArray("status")?.Select(s => Enum.Parse<Client.Models.PaymentRequestStatus>(s, true)).ToArray(),
                IncludeArchived = fs.GetFilterBool("includearchived") ?? false,
                SearchText = model.SearchText,
                StartDate = startDate,
                EndDate = endDate,
                LabelFilter = model.LabelFilter
            });

            model.Search = fs;
            model.SearchText = textSearch;

            var items = result.Select(data => new ViewPaymentRequestViewModel(data)
            {
                AmountFormatted = _displayFormatter.Currency(data.Amount, data.Currency)
            }).ToList();

            var paymentRequestIds = items.Select(i => i.Id).ToArray();
            var labelsByPaymentRequestId =
                await _walletRepository.GetWalletLabelsForObjects(walletId, WalletObjectData.Types.PaymentRequest, paymentRequestIds);

            foreach (var item in items)
            {
                if (labelsByPaymentRequestId.TryGetValue(item.Id, out var labelTuples))
                {
                    item.Labels = labelTuples.Select(l => new TransactionTagModel
                    {
                        Text = l.Label,
                        Color = l.Color,
                        TextColor = ColorPalette.Default.TextColor(l.Color)
                    }).ToList();
                }
                else
                {
                    item.Labels = new List<TransactionTagModel>();
                }
            }

            var allLabels = await _walletRepository.GetWalletLabelsByLinkedType(walletId, WalletObjectData.Types.PaymentRequest);
            model.Labels = allLabels
                .Select(l => new TransactionTagModel
                {
                    Text = l.Label,
                    Color = l.Color,
                    TextColor = ColorPalette.Default.TextColor(l.Color)
                })
                .OrderBy(l => l.Text)
                .ToList();

            model.Items = items;
            return View(model);
        }

        [HttpGet("/stores/{storeId}/payment-requests/edit/{payReqId?}")]
        [Authorize(Policy = Policies.CanModifyPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> EditPaymentRequest(string storeId, string payReqId)
        {
            var store = GetCurrentStore();
            if (store == null)
            {
                return NotFound();
            }

            var paymentRequest = GetCurrentPaymentRequest();
            if (paymentRequest == null && !string.IsNullOrEmpty(payReqId))
            {
                return NotFound();
            }

            if (!store.AnyPaymentMethodAvailable(_handlers))
            {
                return NoPaymentMethodResult(storeId);
            }

            var storeBlob = store.GetStoreBlob();
            var prInvoices = payReqId is null ? null : (await _PaymentRequestService.GetPaymentRequest(payReqId, GetUserId())).Invoices;
            var vm = new UpdatePaymentRequestViewModel(paymentRequest)
            {
                StoreId = store.Id,
                AmountAndCurrencyEditable = payReqId is null || !prInvoices.Any()
            };

            vm.Currency ??= storeBlob.DefaultCurrency;
            vm.HasEmailRules = await HasEmailRules(store.Id);

            if (!string.IsNullOrEmpty(payReqId))
            {
                var defaultNetwork = _networkProvider.DefaultNetwork;
                var walletId = new WalletId(store.Id, defaultNetwork.CryptoCode);
                var labels = await _walletRepository.GetWalletLabelsForObjects(walletId, WalletObjectData.Types.PaymentRequest, new[] { payReqId });
                if (labels.TryGetValue(payReqId, out var labelTuples))
                {
                    vm.Labels = labelTuples.Select(l => l.Label).ToList();
                }
            }

            return View(nameof(EditPaymentRequest), vm);
        }

        private async Task<bool> HasEmailRules(string storeId)
        {
            await using var ctx = _dbContextFactory.CreateContext();
            return await ctx.Set<EmailRuleData>()
                .AsNoTracking()
                .AnyAsync(r => r.StoreId == storeId && EF.Functions.Like(r.Trigger, "WH-PaymentRequest%"));
        }

        [HttpPost("/stores/{storeId}/payment-requests/edit/{payReqId?}")]
        [Authorize(Policy = Policies.CanModifyPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> EditPaymentRequest(string payReqId, UpdatePaymentRequestViewModel viewModel)
        {
            viewModel.Id = payReqId;
            if (!string.IsNullOrEmpty(viewModel.Currency) &&
                _Currencies.GetCurrencyData(viewModel.Currency, false) == null)
                ModelState.AddModelError(nameof(viewModel.Currency), "Invalid currency");

            if (string.IsNullOrEmpty(viewModel.Currency))
                viewModel.Currency = null;

            var store = GetCurrentStore();
            var paymentRequest = GetCurrentPaymentRequest();

            if ((paymentRequest == null && !string.IsNullOrEmpty(payReqId)) ||
                (paymentRequest != null && paymentRequest.Id != payReqId))
                return NotFound();

            if (!store.AnyPaymentMethodAvailable(_handlers))
                return NoPaymentMethodResult(store.Id);

            if (paymentRequest?.Archived is true && viewModel.Archived)
                ModelState.AddModelError(string.Empty, StringLocalizer["You cannot edit an archived payment request."]);

            // Validate ReferenceId is unique for this store (for both new and edit)
            if (!string.IsNullOrEmpty(viewModel.ReferenceId))
            {
                var existingPaymentRequests = await _PaymentRequestRepository.FindPaymentRequests(
                    new PaymentRequestQuery
                    {
                        StoreId = viewModel.StoreId,
                        SearchText = viewModel.ReferenceId
                    });

                var duplicate = existingPaymentRequests.FirstOrDefault(pr => pr.ReferenceId == viewModel.ReferenceId && pr.Id != payReqId);

                if (duplicate != null)
                    ModelState.AddModelError(nameof(viewModel.ReferenceId),
                        StringLocalizer["A payment request with reference ID \"{0}\" already exists for this store.", viewModel.ReferenceId].Value);
            }

            if (!ModelState.IsValid)
            {
                // Rockstar: This code is kinda ugly but needed to show the email rules warning again
                viewModel.HasEmailRules = await HasEmailRules(store.Id);
                return View(nameof(EditPaymentRequest), viewModel);
            }


            var data = paymentRequest ?? new PaymentRequestData();
            data.StoreDataId = viewModel.StoreId;
            data.Archived = viewModel.Archived;
            var blob = data.GetBlob();

            var prInvoices = payReqId is null ? [] : (await _PaymentRequestService.GetPaymentRequest(payReqId, GetUserId())).Invoices;
            viewModel.AmountAndCurrencyEditable = payReqId is null || !prInvoices.Any();
            if (!viewModel.AmountAndCurrencyEditable)
            {
                ModelState.Remove(nameof(data.Amount));
                ModelState.Remove(nameof(data.Currency));
                viewModel.Amount = data.Amount;
                viewModel.Currency = data.Currency;
            }

            data.Title = viewModel.Title;
            blob.Email = viewModel.Email;
            blob.Description = viewModel.Description;
            data.Amount = viewModel.Amount;
            data.Currency = viewModel.Currency ?? store.GetStoreBlob().DefaultCurrency;
            data.Expiry = viewModel.ExpiryDate?.ToUniversalTime();
            data.ReferenceId = viewModel.ReferenceId;
            blob.AllowCustomPaymentAmounts = viewModel.AllowCustomPaymentAmounts;
            blob.FormId = viewModel.FormId;
            if (payReqId is null || blob.RequestBaseUrl is null)
                blob.RequestBaseUrl = Request.GetRequestBaseUrl().ToString();

            data.SetBlob(blob);
            var isNewPaymentRequest = string.IsNullOrEmpty(payReqId);
            if (isNewPaymentRequest)
                data.Created = DateTimeOffset.UtcNow;

            data = await _PaymentRequestRepository.CreateOrUpdatePaymentRequest(data);

            var defaultNetwork = _networkProvider.DefaultNetwork;
            var walletId = new WalletId(store.Id, defaultNetwork.CryptoCode);
            var walletObjectId = new WalletObjectId(walletId, WalletObjectData.Types.PaymentRequest, data.Id);
            
            if (!isNewPaymentRequest)
            {
                var existingLabels = await _walletRepository.GetWalletLabelsForObjects(walletId, WalletObjectData.Types.PaymentRequest, new[] { data.Id });
                if (existingLabels.TryGetValue(data.Id, out var labelTuples))
                {
                    var currentLabels = labelTuples.Select(l => l.Label).ToArray();
                    var toRemove = currentLabels.Where(label => !viewModel.Labels.Contains(label)).ToArray();
                    if (toRemove.Any())
                    {
                        await _walletRepository.RemoveWalletObjectLabels(walletObjectId, toRemove);
                    }
                }
            }
            
            if (viewModel.Labels.Any())
            {
                await _walletRepository.AddWalletObjectLabels(walletObjectId, viewModel.Labels.ToArray());
            }

            TempData[WellKnownTempData.SuccessMessage] = isNewPaymentRequest
                ? StringLocalizer["Payment request \"{0}\" created successfully", viewModel.Title].Value
                : StringLocalizer["Payment request \"{0}\" updated successfully", viewModel.Title].Value;
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
            vm.HubPath = PaymentRequestHub.GetHubPath(Request);
            vm.StoreName = store.StoreName;
            vm.StoreWebsite = store.StoreWebsite;
            vm.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob);

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
                    if (string.IsNullOrEmpty(prBlob.Email) && form.GetFieldByFullName("buyerEmail") is { } emailField)
                    {
                        prBlob.Email = emailField.Value;
                    }
                    if (prBlob.RequestBaseUrl is null)
                        prBlob.RequestBaseUrl = Request.GetRequestBaseUrl().ToString();

                    result.SetBlob(prBlob);
                    await _PaymentRequestRepository.CreateOrUpdatePaymentRequest(result);
                    return RedirectToAction("PayPaymentRequest", new { payReqId });
                }
            }

            viewModel.FormName = formData.Name;
            viewModel.Form = form;

            var storeBlob = result.StoreData.GetStoreBlob();
            viewModel.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob);

            return View("Views/UIForms/View", viewModel);
        }

        [HttpGet("{payReqId}/pay")]
        [AllowAnonymous]
        public async Task<IActionResult> PayPaymentRequest(string payReqId, bool redirectToInvoice = true,
            decimal? amount = null, CancellationToken cancellationToken = default)
        {
            if (amount.HasValue && amount.Value <= 0)
            {
                return BadRequest(StringLocalizer["Please provide an amount greater than 0"]);
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

                return BadRequest(StringLocalizer["Payment Request cannot be paid as it has been archived"]);
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

                return BadRequest(StringLocalizer["Payment Request has already been settled."]);
            }

            if (result.ExpiryDate.HasValue && DateTime.UtcNow >= result.ExpiryDate)
            {
                if (redirectToInvoice)
                {
                    return RedirectToAction("ViewPaymentRequest", new { payReqId });
                }

                return BadRequest(StringLocalizer["Payment Request has expired"]);
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
                var prData = await _PaymentRequestRepository.FindPaymentRequest(result.Id, null, cancellationToken);
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
        [AllowAnonymous]
        public async Task<IActionResult> CancelUnpaidPendingInvoice(string payReqId, bool redirect = true)
        {
            var result = await _PaymentRequestService.GetPaymentRequest(payReqId, GetUserId());
            if (result == null)
            {
                return NotFound();
            }

            if (!result.AllowCustomPaymentAmounts)
            {
                return BadRequest(StringLocalizer["Not allowed to cancel this invoice"]);
            }

            var invoices = result.Invoices.Where(requestInvoice =>
                requestInvoice.State.Status == InvoiceStatus.New && !requestInvoice.Payments.Any());

            if (!invoices.Any())
            {
                return BadRequest(StringLocalizer["No unpaid pending invoice to cancel"]);
            }

            foreach (var invoice in invoices)
            {
                await _InvoiceRepository.MarkInvoiceStatus(invoice.Id, InvoiceStatus.Invalid);
            }

            if (redirect)
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Payment cancelled"].Value;
                return RedirectToAction(nameof(ViewPaymentRequest), new { payReqId });
            }

            return Ok(StringLocalizer["Payment cancelled"]);
        }

        [HttpGet("{payReqId}/clone")]
        [Authorize(Policy = Policies.CanModifyPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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
        [Authorize(Policy = Policies.CanModifyPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> TogglePaymentRequestArchival(string payReqId)
        {
            var store = GetCurrentStore();

            var result = await _PaymentRequestRepository.ArchivePaymentRequest(payReqId, true);
            if (result is not null)
            {
                TempData[WellKnownTempData.SuccessMessage] = result.Value
                    ? StringLocalizer["The payment request has been archived and will no longer appear in the payment request list by default again."].Value
                    : StringLocalizer["The payment request has been unarchived and will appear in the payment request list by default."].Value;
                return RedirectToAction("GetPaymentRequests", new { storeId = store.Id });
            }

            return NotFound();
        }

        [HttpPost("{payReqId}/complete")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyPaymentRequests)]
        public async Task<IActionResult> TogglePaymentRequestCompleted(string payReqId)
        {
            if (string.IsNullOrWhiteSpace(payReqId))
            {
                return BadRequest("Invalid parameters");
            }

            var paymentRequest = await _PaymentRequestRepository.FindPaymentRequest(payReqId, GetUserId());
            if (paymentRequest == null)
            {
                return NotFound();
            }

            if (paymentRequest.Status != PaymentRequestStatus.Pending)
            {
                return BadRequest("Invalid payment request status. Only pending payment requests can be marked as completed.");
            }

            await _PaymentRequestRepository.UpdatePaymentRequestStatus(payReqId, PaymentRequestStatus.Completed);

            return RedirectToAction("GetPaymentRequests", new { storeId = paymentRequest.StoreDataId });
        }

        [HttpGet("/stores/{storeId}/payment-requests/labels")]
        [Authorize(Policy = Policies.CanViewPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> PaymentRequestLabels(string storeId)
        {
            var store = GetCurrentStore();
            var defaultNetwork = _networkProvider.DefaultNetwork;
            var walletId = new WalletId(store.Id, defaultNetwork.CryptoCode);
            
            var labels = await _walletRepository.GetWalletLabelsByLinkedType(walletId, WalletObjectData.Types.PaymentRequest);

            var vm = new PaymentRequestLabelsViewModel
            {
                StoreId = storeId,
                Labels = labels
                    .Where(l => !WalletObjectData.Types.AllTypes.Contains(l.Label))
                    .Select(tuple => new PaymentRequestLabelViewModel
                    {
                        Label = tuple.Label,
                        Color = tuple.Color,
                        TextColor = ColorPalette.Default.TextColor(tuple.Color)
                    })
            };

            return View(vm);
        }

        [HttpPost("/stores/{storeId}/payment-requests/labels/{id}/delete")]
        [Authorize(Policy = Policies.CanModifyPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> DeletePaymentRequestLabel(string storeId, string id)
        {
            var store = GetCurrentStore();
            var defaultNetwork = _networkProvider.DefaultNetwork;
            var walletId = new WalletId(store.Id, defaultNetwork.CryptoCode);
            var labels = new[] { id };
            
            if (await _walletRepository.RemoveWalletLabels(walletId, labels))
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The label has been successfully deleted."].Value;
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["The label could not be deleted."].Value;
            }

            return RedirectToAction(nameof(PaymentRequestLabels), new { storeId });
        }

        [HttpPost("/stores/{storeId}/payment-requests/labels/{id}/edit")]
        [Authorize(Policy = Policies.CanModifyPaymentRequests, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> EditPaymentRequestLabel(string storeId, string id, string newLabel)
        {
            if (string.IsNullOrWhiteSpace(newLabel))
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Label name cannot be empty."].Value;
                return RedirectToAction(nameof(PaymentRequestLabels), new { storeId });
            }

            newLabel = newLabel.Trim();
            if (newLabel == id)
            {
                return RedirectToAction(nameof(PaymentRequestLabels), new { storeId });
            }

            var store = GetCurrentStore();
            var defaultNetwork = _networkProvider.DefaultNetwork;
            var walletId = new WalletId(store.Id, defaultNetwork.CryptoCode);

            if (await _walletRepository.RenameWalletLabel(walletId, id, newLabel))
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The label has been successfully renamed."].Value;
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["The label could not be renamed."].Value;
            }

            return RedirectToAction(nameof(PaymentRequestLabels), new { storeId });
        }

        private string GetUserId() => _UserManager.GetUserId(User);

        private StoreData GetCurrentStore() => HttpContext.GetStoreData();

        private PaymentRequestData GetCurrentPaymentRequest() => HttpContext.GetPaymentRequestData();

        private IActionResult NoPaymentMethodResult(string storeId)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Html =
                    $"To create a payment request, you need to <a href='{Url.Action(nameof(UIStoresController.SetupWallet), "UIStores", new { cryptoCode = _networkProvider.DefaultNetwork.CryptoCode, storeId })}' class='alert-link'>set up a wallet</a> first",
                AllowDismiss = false
            });
            return RedirectToAction(nameof(GetPaymentRequests), new { storeId });
        }
    }
}
