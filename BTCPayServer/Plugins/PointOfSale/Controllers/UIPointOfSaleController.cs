using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Forms;
using BTCPayServer.Forms.Models;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using NicolasDorier.RateLimits;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.PointOfSale.Controllers
{
    [AutoValidateAntiforgeryToken]
    [Route("apps")]
    public class UIPointOfSaleController : Controller
    {
        public UIPointOfSaleController(
            AppService appService,
            CurrencyNameTable currencies,
            StoreRepository storeRepository,
            UriResolver uriResolver,
            InvoiceRepository invoiceRepository,
            UIInvoiceController invoiceController,
            FormDataService formDataService,
            IStringLocalizer stringLocalizer,
            DisplayFormatter displayFormatter,
            IRateLimitService rateLimitService,
            IAuthorizationService authorizationService,
            UserManager<ApplicationUser> userManager,
            Safe safe)
        {
            _currencies = currencies;
            _appService = appService;
            _storeRepository = storeRepository;
            _uriResolver = uriResolver;
            _invoiceRepository = invoiceRepository;
            _invoiceController = invoiceController;
            _displayFormatter = displayFormatter;
            _rateLimitService = rateLimitService;
            _authorizationService = authorizationService;
            _userManager = userManager;
            _safe = safe;
            StringLocalizer = stringLocalizer;
            FormDataService = formDataService;
        }

        private readonly CurrencyNameTable _currencies;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly StoreRepository _storeRepository;
        private readonly UriResolver _uriResolver;
        private readonly AppService _appService;
        private readonly UIInvoiceController _invoiceController;
        private readonly DisplayFormatter _displayFormatter;
        private readonly IRateLimitService _rateLimitService;
        private readonly IAuthorizationService _authorizationService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly Safe _safe;
        public FormDataService FormDataService { get; }
        public IStringLocalizer StringLocalizer { get; }

        [HttpGet("/")]
        [HttpGet("/apps/{appId}/pos")]
        [HttpGet("/apps/{appId}/pos/{viewType?}")]
        [DomainMappingConstraint(PointOfSaleAppType.AppType)]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> ViewPointOfSale(string appId, PosViewType? viewType = null)
        {
            var app = await _appService.GetApp(appId, PointOfSaleAppType.AppType);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<PointOfSaleSettings>();
            var numberFormatInfo = _appService.Currencies.GetNumberFormatInfo(settings.Currency) ??
                                   _appService.Currencies.GetNumberFormatInfo("USD");
            double step = Math.Pow(10, -numberFormatInfo.CurrencyDecimalDigits);
            viewType ??= settings.EnableShoppingCart ? PosViewType.Cart : settings.DefaultView;
            var store = await _appService.GetStore(app);
            var storeBlob = store.GetStoreBlob();
            var storeBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob);

            return View($"PointOfSale/Public/{viewType}", new ViewPointOfSaleViewModel
            {
                Title = settings.Title,
                StoreName = store.StoreName,
                StoreBranding = storeBranding,
                Step = step.ToString(CultureInfo.InvariantCulture),
                ViewType = (PosViewType)viewType,
                ShowItems = settings.ShowItems,
                ShowCustomAmount = settings.ShowCustomAmount,
                ShowDiscount = settings.ShowDiscount,
                ShowSearch = settings.ShowSearch,
                ShowCategories = settings.ShowCategories,
                EnableTips = settings.EnableTips,
                CurrencyCode = settings.Currency,
                CurrencySymbol = numberFormatInfo.CurrencySymbol,
                CurrencyInfo = new ViewPointOfSaleViewModel.CurrencyInfoData
                {
                    CurrencySymbol = string.IsNullOrEmpty(numberFormatInfo.CurrencySymbol) ? settings.Currency : numberFormatInfo.CurrencySymbol,
                    Divisibility = numberFormatInfo.CurrencyDecimalDigits,
                    DecimalSeparator = numberFormatInfo.CurrencyDecimalSeparator,
                    ThousandSeparator = numberFormatInfo.NumberGroupSeparator,
                    Prefixed = new[] { 0, 2 }.Contains(numberFormatInfo.CurrencyPositivePattern),
                    SymbolSpace = new[] { 2, 3 }.Contains(numberFormatInfo.CurrencyPositivePattern)
                },
                Items = AppService.Parse(settings.Template, false),
                ButtonText = settings.ButtonText,
                CustomButtonText = settings.CustomButtonText,
                CustomTipText = settings.CustomTipText,
                CustomTipPercentages = settings.CustomTipPercentages,
                DefaultTaxRate =  settings.DefaultTaxRate,
                AppId = appId,
                StoreId = store.Id,
                HtmlLang = settings.HtmlLang,
                HtmlMetaTags= settings.HtmlMetaTags,
                Description = settings.Description,
            });
        }

        [HttpPost("/")]
        [HttpPost("/apps/{appId}/pos/{viewType?}")]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [DomainMappingConstraint(PointOfSaleAppType.AppType)]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> ViewPointOfSale(string appId,
                                                        PosViewType? viewType = null,
                                                        [ModelBinder(typeof(InvariantDecimalModelBinder))] decimal? amount = null,
                                                        [ModelBinder(typeof(InvariantDecimalModelBinder))] decimal? tip = null,
                                                        [ModelBinder(typeof(InvariantDecimalModelBinder))] decimal? discount = null,
                                                        [ModelBinder(typeof(InvariantDecimalModelBinder))] decimal? customAmount = null,
                                                        string email = null,
                                                        string orderId = null,
                                                        string notificationUrl = null,
                                                        string redirectUrl = null,
                                                        string choiceKey = null,
                                                        string posData = null,
                                                        string formResponse = null,
                                                        CancellationToken cancellationToken = default)
        {
            if (await Throttle(appId))
                return new TooManyRequestsResult(ZoneLimits.PublicInvoices);

            // Distinguish JSON requests coming via the mobile app
            var wantsJson = Request.Headers.Accept.FirstOrDefault()?.StartsWith("application/json") is true;

            IActionResult Error(string message)
            {
                if (wantsJson)
                    return Json(new { error = message });
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = message,
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    AllowDismiss = true
                });
                return RedirectToAction(nameof(ViewPointOfSale), new { appId });
            }

            var app = await _appService.GetApp(appId, PointOfSaleAppType.AppType);
            if (app == null)
                return wantsJson
                    ? Json(new { error = StringLocalizer["App not found"].Value })
                    : NotFound();

            // not allowing negative tips or discounts
            if (tip < 0 || discount < 0)
                return Error(StringLocalizer["Negative tip or discount is not allowed"].Value);

            if (string.IsNullOrEmpty(choiceKey) && (amount < 0 || customAmount < 0))
                return Error(StringLocalizer["Negative amount is not allowed"].Value);

            var settings = app.GetSettings<PointOfSaleSettings>();
            settings.DefaultView = settings.EnableShoppingCart ? PosViewType.Cart : settings.DefaultView;
            var currentView = viewType ?? settings.DefaultView;
            if (string.IsNullOrEmpty(choiceKey) && !settings.ShowCustomAmount &&
                currentView != PosViewType.Cart && currentView != PosViewType.Light)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId, viewType });
            }
            var choices = AppService.Parse(settings.Template, false);
            var jposData = PosAppData.TryParse(posData) ?? new();
            PoSOrder order = new(_currencies.GetNumberFormatInfo(settings.Currency, true).CurrencyDecimalDigits);
            Dictionary<string, InvoiceSupportedTransactionCurrency> paymentMethods = null;
            List<AppItem> selectedChoices = new();
            if (!string.IsNullOrEmpty(choiceKey))
            {
                jposData.Cart = new PosAppCartItem[] { new() { Id = choiceKey, Count = 1, Price = amount ?? 0 } };
            }

            jposData.Cart ??= [];

            if (currentView is PosViewType.Print)
                return NotFound();
            if (currentView is PosViewType.Cart && jposData.Cart.Length == 0)
                return NotFound();

            if (string.IsNullOrEmpty(choiceKey) &&
                jposData.Amounts is null &&
                amount is { } o)
            {
                order.AddLine(new("", 1, o, settings.DefaultTaxRate));
            }
            for (var i = 0; i < (jposData.Amounts ?? []).Length; i++)
            {
                order.AddLine(new($"Custom Amount {i + 1}", 1, jposData.Amounts[i], settings.DefaultTaxRate));
            }

            foreach (var cartItem in jposData.Cart)
            {
                var itemChoice = choices.FirstOrDefault(item => item.Id == cartItem.Id);
                if (itemChoice == null)
                    return NotFound();
                selectedChoices.Add(itemChoice);
                if (itemChoice.Inventory is <= 0 ||
                    itemChoice.Inventory is { } inv && inv < cartItem.Count)
                    return Error(StringLocalizer["Inventory for {0} exhausted: {1} available", itemChoice.Title, itemChoice.Inventory]);

                if (itemChoice.PriceType is not AppItemPriceType.Topup)
                {
                    var expectedCartItemPrice = itemChoice.Price ?? 0;
                    if (cartItem.Price < expectedCartItemPrice)
                        cartItem.Price = expectedCartItemPrice;
                }
                order.AddLine(new(cartItem.Id, cartItem.Count, cartItem.Price, itemChoice.TaxRate ?? settings.DefaultTaxRate));
            }
            if (customAmount is { } c && settings.ShowCustomAmount)
                order.AddLine(new("", 1, c, settings.DefaultTaxRate));
            if (discount is { } d)
                order.AddDiscountRate(d);
            if (tip is { } t)
                order.AddTip(t);

            var store = await _appService.GetStore(app);
            var storeBlob = store.GetStoreBlob();
            var posFormId = settings.FormId;

            // skip forms feature for JSON requests (from the app)
            var formData = wantsJson ? null : await FormDataService.GetForm(posFormId);
            JObject formResponseJObject = null;
            switch (formData)
            {
                case null:
                    break;
                case not null:
                    if (formResponse is null)
                    {
                        var vm = new PostRedirectViewModel
                        {
                            FormUrl = Url.Action(nameof(POSForm), "UIPointOfSale", new { appId, buyerEmail = email }),
                            FormParameters = new MultiValueDictionary<string, string>(Request.Form.Select(pair =>
                                new KeyValuePair<string, IReadOnlyCollection<string>>(pair.Key, pair.Value)))
                        };
                        if (viewType.HasValue)
                        {
                            vm.RouteParameters.Add("viewType", viewType.Value.ToString());
                        }

                        return View("PostRedirect", vm);
                    }

                    formResponseJObject = TryParseJObject(formResponse) ?? new JObject();
                    var form = Form.Parse(formData.Config);
                    FormDataService.SetValues(form, formResponseJObject);
                    if (!FormDataService.Validate(form, ModelState))
                    {
                        //someone tried to bypass validation
                        return RedirectToAction(nameof(ViewPointOfSale), new { appId, viewType });
                    }

                    var amtField = form.GetFieldByFullName($"{FormDataService.InvoiceParameterPrefix}amount");
                    if (amtField is null)
                    {
                        amtField = new Field
                        {
                            Name = $"{FormDataService.InvoiceParameterPrefix}amount",
                            Type = "hidden",
                            Constant = true
                        };
                        form.Fields.Add(amtField);
                    }
                    var originalAmount = order.Calculate().PriceTaxExcluded;
                    amtField.Value = originalAmount.ToString(CultureInfo.InvariantCulture);
                    formResponseJObject = FormDataService.GetValues(form);

                    var invoiceRequest = FormDataService.GenerateInvoiceParametersFromForm(form);
                    // If the form has an amount field, we compute the difference from the original POS order amount, and add it as a line item
                    if (invoiceRequest.Amount is not null && originalAmount != invoiceRequest.Amount.Value )
                    {
                        var diff = invoiceRequest.Amount.Value - originalAmount;
                        order.AddLine(new("", 1, diff, settings.DefaultTaxRate));
                    }
                    break;
            }

            var summary = order.Calculate();
            var isTopup = currentView == PosViewType.Static &&
                          selectedChoices.Any(c => c.PriceType == AppItemPriceType.Topup);

            var receiptData = PosReceiptData.Create(isTopup, selectedChoices, jposData, order, summary, settings.Currency, _displayFormatter);
            if (!isTopup && summary.PriceTaxIncludedWithTips <= 0m && settings.DisableZeroAmountInvoice is true)
                return Error(StringLocalizer["Zero amount invoices are disabled"].Value);

            try
            {
                var invoice = await _invoiceController.CreateInvoiceCoreRaw(new CreateInvoiceRequest
                {
                    Amount = isTopup ? null : summary.PriceTaxIncludedWithTips,
                    Currency = settings.Currency,
                    Metadata = new InvoiceMetadata
                    {
                        ItemCode = selectedChoices is [{} c1] ? c1.Id : null,
                        ItemDesc = selectedChoices is [{} c2] ? c2.Title : null,
                        BuyerEmail = email,
                        TaxIncluded = summary.Tax == 0m ? null : summary.Tax,
                        OrderId = orderId ?? AppService.GetRandomOrderId(),
                        OrderUrl = Request.GetDisplayUrl(),
                        PosData = JObject.FromObject(jposData),
                        ReceiptData = receiptData
                    }.ToJObject(),
                    Checkout = new InvoiceDataBase.CheckoutOptions()
                    {
                        RedirectAutomatically = settings.RedirectAutomatically,
                        RedirectURL = !string.IsNullOrEmpty(redirectUrl) ? redirectUrl
                            : !string.IsNullOrEmpty(settings.RedirectUrl) ? settings.RedirectUrl
                            : Url.ActionAbsolute(Request, nameof(ViewPointOfSale), "UIPointOfSale", new { appId, viewType }).ToString(),
                        PaymentMethods = paymentMethods?.Where(p => p.Value.Enabled).Select(p => p.Key).ToArray()
                    },
                    AdditionalSearchTerms = new[] { AppService.GetAppSearchTerm(app) }
                }, store, HttpContext.Request.GetAbsoluteRoot(),
                    new List<string> { AppService.GetAppInternalTag(appId) },
                    cancellationToken, entity =>
                    {
                        entity.NotificationURLTemplate =
                            string.IsNullOrEmpty(notificationUrl) ? settings.NotificationUrl : notificationUrl;
                        entity.FullNotifications = true;
                        entity.ExtendedNotifications = true;
                        if (formResponseJObject is not null)
                        {
                            var meta = entity.Metadata.ToJObject();
                            meta.Merge(formResponseJObject);
                            entity.Metadata = InvoiceMetadata.FromJObject(meta);
                        }
                    });
                var data = new { invoiceId = invoice.Id };
                if (wantsJson)
                    return Json(data);
                if (!isTopup && summary.PriceTaxIncludedWithTips is 0 && storeBlob.ReceiptOptions?.Enabled is true)
                    return RedirectToAction(nameof(UIInvoiceController.InvoiceReceipt), "UIInvoice", data);
                return RedirectToAction(nameof(UIInvoiceController.Checkout), "UIInvoice", data);
            }
            catch (BitpayHttpException e)
            {
                if (wantsJson) return Json(new { error = e.Message });
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Html = e.Message.Replace("\n", "<br />", StringComparison.OrdinalIgnoreCase),
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    AllowDismiss = true
                });
                return RedirectToAction(nameof(ViewPointOfSale), new { appId });
            }
        }

        private async Task<bool> Throttle(string appId) =>
            !(await _authorizationService.AuthorizeAsync(HttpContext.User, appId, Policies.CanViewInvoices)).Succeeded &&
            HttpContext.Connection is { RemoteIpAddress: { } addr } &&
            !await _rateLimitService.Throttle(ZoneLimits.PublicInvoices, addr.ToString(), HttpContext.RequestAborted);

        private JObject TryParseJObject(string posData)
        {
            try
            {
                return JObject.Parse(posData);
            }
            catch
            {
            }
            return null;
        }

        [HttpPost("/apps/{appId}/pos/form/{viewType?}")]
        [IgnoreAntiforgeryToken]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> POSForm(string appId, PosViewType? viewType = null)
        {
            var app = await _appService.GetApp(appId, PointOfSaleAppType.AppType);
            if (app == null)
                return NotFound();

            var settings = app.GetSettings<PointOfSaleSettings>();
            var formData = await FormDataService.GetForm(settings.FormId);
            if (formData is null)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId, viewType });
            }

            var prefix = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)) + "_";
            var formParameters = Request.Form
                .Where(pair => pair.Key != "__RequestVerificationToken")
                .ToMultiValueDictionary(p => p.Key, p => p.Value.ToString());
            var controller = nameof(UIPointOfSaleController).TrimEnd("Controller", StringComparison.InvariantCulture);
            var store = await _appService.GetStore(app);
            var storeBlob = store.GetStoreBlob();
            var form = Form.Parse(formData.Config);
            form.ApplyValuesFromForm(Request.Query);
            var vm = new FormViewModel
            {
                StoreName = store.StoreName,
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob),
                FormName = formData.Name,
                Form = form,
                AspController = controller,
                AspAction = nameof(POSFormSubmit),
                RouteParameters = new Dictionary<string, string> { { "appId", appId } },
                FormParameters = formParameters,
                FormParameterPrefix = prefix
            };
            if (viewType.HasValue)
            {
                vm.RouteParameters.Add("viewType", viewType.Value.ToString());
            }

            return View("Views/UIForms/View", vm);
        }

        [HttpPost("/apps/{appId}/pos/form/submit/{viewType?}")]
        [IgnoreAntiforgeryToken]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
        public async Task<IActionResult> POSFormSubmit(string appId, FormViewModel viewModel, PosViewType? viewType = null)
        {
            var app = await _appService.GetApp(appId, PointOfSaleAppType.AppType);
            if (app == null)
                return NotFound();

            var settings = app.GetSettings<PointOfSaleSettings>();
            var formData = await FormDataService.GetForm(settings.FormId);
            if (formData is null)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId, viewType });
            }
            var form = Form.Parse(formData.Config);
            var formFieldNames = form.GetAllFields().Select(tuple => tuple.FullName).Distinct().ToArray();
            var formParameters = Request.Form
                .Where(pair => pair.Key.StartsWith(viewModel.FormParameterPrefix))
                .ToDictionary(pair => pair.Key.Replace(viewModel.FormParameterPrefix, string.Empty), pair => pair.Value)
                .ToMultiValueDictionary(p => p.Key, p => p.Value.ToString());

            if (Request is { Method: "POST", HasFormContentType: true })
            {
                form.ApplyValuesFromForm(Request.Form.Where(pair => formFieldNames.Contains(pair.Key)));

                if (FormDataService.Validate(form, ModelState))
                {
                    var controller = nameof(UIPointOfSaleController).TrimEnd("Controller", StringComparison.InvariantCulture);
                    var redirectUrl =
                        Url.ActionAbsolute(Request, nameof(ViewPointOfSale), controller, new { appId, viewType }).ToString();
                    formParameters.Add("formResponse", FormDataService.GetValues(form).ToString());
                    return View("PostRedirect", new PostRedirectViewModel
                    {
                        FormUrl = redirectUrl,
                        FormParameters = formParameters
                    });
                }
            }

            var store = await _appService.GetStore(app);
            var storeBlob = store.GetStoreBlob();

            viewModel.FormName = formData.Name;
            viewModel.Form = form;
            viewModel.FormParameters = formParameters;
            viewModel.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob);
            return View("Views/UIForms/View", viewModel);
        }

        [Authorize(Policy = Policies.CanViewInvoices, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("/apps/{appId}/pos/recent-transactions")]
        public async Task<IActionResult> RecentTransactions(string appId)
        {
            var app = await _appService.GetApp(appId, PointOfSaleAppType.AppType);
            if (app == null)
                return NotFound();

            var from = DateTimeOffset.UtcNow - TimeSpan.FromDays(3);
            var invoices = await AppService.GetInvoicesForApp(_invoiceRepository, app, from);
            var recent = invoices
                .Take(10)
                .Select(i => new JObject
                {
                    ["id"] = i.Id,
                    ["date"] = i.InvoiceTime,
                    ["price"] = _displayFormatter.Currency(i.Price, i.Currency, DisplayFormatter.CurrencyFormat.Symbol),
                    ["status"] = i.GetInvoiceState().ToString(),
                    ["url"] = Url.Action(nameof(UIInvoiceController.Invoice), "UIInvoice", new { invoiceId = i.Id })
                });
            return Json(recent);
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("{appId}/settings/pos")]
        public async Task<IActionResult> UpdatePointOfSale(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            var settings = app.GetSettings<PointOfSaleSettings>();
            settings.DefaultView = settings.EnableShoppingCart ? PosViewType.Cart : settings.DefaultView;
            settings.EnableShoppingCart = false;

            var vm = new UpdatePointOfSaleViewModel
            {
                Id = appId,
                StoreId = app.StoreDataId,
                StoreName = app.StoreData?.StoreName,
                StoreDefaultCurrency = await GetStoreDefaultCurrentIfEmpty(app.StoreDataId, settings.Currency),
                Archived = app.Archived,
                AppName = app.Name,
                Title = settings.Title,
                DefaultTaxRate = settings.DefaultTaxRate,
                DefaultView = settings.DefaultView,
                ShowItems = settings.ShowItems,
                ShowCustomAmount = settings.ShowCustomAmount,
                ShowDiscount = settings.ShowDiscount,
                ShowSearch = settings.ShowSearch,
                ShowCategories = settings.ShowCategories,
                EnableTips = settings.EnableTips,
                Currency = settings.Currency,
                Template = settings.Template,
                ButtonText = settings.ButtonText ?? PointOfSaleSettings.BUTTON_TEXT_DEF,
                CustomButtonText = settings.CustomButtonText ?? PointOfSaleSettings.CUSTOM_BUTTON_TEXT_DEF,
                CustomTipText = settings.CustomTipText ?? PointOfSaleSettings.CUSTOM_TIP_TEXT_DEF,
                CustomTipPercentages = settings.CustomTipPercentages != null ? string.Join(",", settings.CustomTipPercentages) : string.Join(",", PointOfSaleSettings.CUSTOM_TIP_PERCENTAGES_DEF),
                HtmlLang = settings.HtmlLang,
                HtmlMetaTags= settings.HtmlMetaTags,
                Description = settings.Description,
                NotificationUrl = settings.NotificationUrl,
                RedirectUrl = settings.RedirectUrl,
                DisableZeroAmountInvoice = settings.DisableZeroAmountInvoice is true,
                SearchTerm = app.TagAllInvoices ? $"storeid:{app.StoreDataId}" : $"appid:{app.Id}",
                RedirectAutomatically = settings.RedirectAutomatically.HasValue ? settings.RedirectAutomatically.Value ? "true" : "false" : "",
                FormId = settings.FormId
            };
            if (HttpContext.Request != null)
            {
                var appUrl = HttpContext.Request.GetAbsoluteUri($"/apps/{appId}/pos");
                var encoder = HtmlEncoder.Default;
                if (settings.ShowCustomAmount)
                {
                    var builder = new StringBuilder();
                    builder.AppendLine(CultureInfo.InvariantCulture, $"<form method=\"POST\" action=\"{encoder.Encode(appUrl)}\">");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"amount\" value=\"100\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"email\" value=\"customer@example.com\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"orderId\" value=\"CustomOrderId\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"notificationUrl\" value=\"https://example.com/callbacks\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"redirectUrl\" value=\"https://example.com/thanksyou\" />");
                    builder.AppendLine($"  <button type=\"submit\">Buy now</button>");
                    builder.AppendLine($"</form>");
                    vm.Example1 = builder.ToString();
                }
                try
                {
                    var items = AppService.Parse(settings.Template);
                    var builder = new StringBuilder();
                    builder.AppendLine(CultureInfo.InvariantCulture, $"<form method=\"POST\" action=\"{encoder.Encode(appUrl)}\">");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"email\" value=\"customer@example.com\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"orderId\" value=\"CustomOrderId\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"notificationUrl\" value=\"https://example.com/callbacks\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"redirectUrl\" value=\"https://example.com/thanksyou\" />");
                    builder.AppendLine(CultureInfo.InvariantCulture, $"  <button type=\"submit\" name=\"choiceKey\" value=\"{items[0].Id}\">Buy now</button>");
                    builder.AppendLine($"</form>");
                    vm.Example2 = builder.ToString();
                }
                catch { }
                vm.InvoiceUrl = appUrl + "invoices/SkdsDghkdP3D3qkj7bLq3";
            }

            vm.ExampleCallback = "{\n  \"id\":\"SkdsDghkdP3D3qkj7bLq3\",\n  \"url\":\"https://btcpay.example.com/invoice?id=SkdsDghkdP3D3qkj7bLq3\",\n  \"status\":\"paid\",\n  \"price\":10,\n  \"currency\":\"EUR\",\n  \"invoiceTime\":1520373130312,\n  \"expirationTime\":1520374030312,\n  \"currentTime\":1520373179327,\n  \"exceptionStatus\":false,\n  \"buyerFields\":{\n    \"buyerEmail\":\"customer@example.com\",\n    \"buyerNotify\":false\n  },\n  \"paymentSubtotals\": {\n    \"BTC\":114700\n  },\n  \"paymentTotals\": {\n    \"BTC\":118400\n  },\n  \"transactionCurrency\": \"BTC\",\n  \"amountPaid\": \"1025900\",\n  \"exchangeRates\": {\n    \"BTC\": {\n      \"EUR\": 8721.690715789999,\n      \"USD\": 10817.99\n    }\n  }\n}";

            await FillUsers(vm);
            return View("PointOfSale/UpdatePointOfSale", vm);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("{appId}/settings/pos")]
        public async Task<IActionResult> UpdatePointOfSale(string appId, UpdatePointOfSaleViewModel vm)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            vm.Id = app.Id;
            if (!ModelState.IsValid)
                return View("PointOfSale/UpdatePointOfSale", vm);

            vm.Currency = await GetStoreDefaultCurrentIfEmpty(app.StoreDataId, vm.Currency);
            if (_currencies.GetCurrencyData(vm.Currency, false) == null)
                ModelState.AddModelError(nameof(vm.Currency), "Invalid currency");
            try
            {
                vm.Template = AppService.SerializeTemplate(AppService.Parse(vm.Template, true, true));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(nameof(vm.Template), $"Invalid template: {ex.Message}");
            }
            if (!ModelState.IsValid)
            {
                await FillUsers(vm);
                return View("PointOfSale/UpdatePointOfSale", vm);
            }

            var settings = new PointOfSaleSettings
            {
                Title = vm.Title,
                DefaultView = vm.DefaultView,
                DefaultTaxRate = vm.DefaultTaxRate ?? 0,
                ShowItems = vm.ShowItems,
                ShowCustomAmount = vm.ShowCustomAmount,
                ShowDiscount = vm.ShowDiscount,
                ShowSearch = vm.ShowSearch,
                ShowCategories = vm.ShowCategories,
                EnableTips = vm.EnableTips,
                Currency = vm.Currency,
                Template = vm.Template,
                ButtonText = vm.ButtonText,
                CustomButtonText = vm.CustomButtonText,
                CustomTipText = vm.CustomTipText,
                CustomTipPercentages = ListSplit(vm.CustomTipPercentages),
                NotificationUrl = vm.NotificationUrl,
                RedirectUrl = vm.RedirectUrl,
                HtmlLang = vm.HtmlLang,
                HtmlMetaTags = _safe.RawMeta(vm.HtmlMetaTags, out bool wasHtmlModified),
                Description = vm.Description,
                DisableZeroAmountInvoice = vm.DisableZeroAmountInvoice,
                RedirectAutomatically = string.IsNullOrEmpty(vm.RedirectAutomatically) ? null : bool.Parse(vm.RedirectAutomatically),
                FormId = vm.FormId
            };

            app.Name = vm.AppName;
            app.Archived = vm.Archived;
            app.SetSettings(settings);
            await _appService.UpdateOrCreateApp(app);
            if (wasHtmlModified)
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Only meta tags are allowed in HTML headers. Your HTML code has been cleaned up accordingly."].Value;
            } else {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["App updated"].Value;
            }
            return RedirectToAction(nameof(UpdatePointOfSale), new { appId });
        }

        private int[] ListSplit(string list, string separator = ",")
        {
            if (string.IsNullOrEmpty(list))
            {
                return Array.Empty<int>();
            }

            // Remove all characters except numeric and comma
            Regex charsToDestroy = new Regex(@"[^\d|\" + separator + "]");
            list = charsToDestroy.Replace(list, "");

            return list.Split(separator, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        }

        private async Task<string> GetStoreDefaultCurrentIfEmpty(string storeId, string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
            {
                currency = (await _storeRepository.FindStore(storeId)).GetStoreBlob().DefaultCurrency;
            }
            return currency.Trim().ToUpperInvariant();
        }

        private StoreData GetCurrentStore() => HttpContext.GetStoreData();

        private AppData GetCurrentApp() => HttpContext.GetAppData();

        private async Task FillUsers(UpdatePointOfSaleViewModel vm)
        {
            var users = await _storeRepository.GetStoreUsers(GetCurrentStore().Id);

            if (!User.IsInRole(Roles.ServerAdmin))
                users = users.Where(u => u.Id == _userManager.GetUserId(User)).ToArray();

            vm.StoreUsers = users.Select(u => (u.Id, u.Email, u.StoreRole.Role))
                .ToDictionary(u => u.Id, u => $"{u.Email} ({u.Role})");
        }
    }
}
