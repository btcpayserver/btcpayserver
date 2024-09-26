using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Abstractions.Models;
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
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
            DisplayFormatter displayFormatter)
        {
            _currencies = currencies;
            _appService = appService;
            _storeRepository = storeRepository;
            _uriResolver = uriResolver;
            _invoiceRepository = invoiceRepository;
            _invoiceController = invoiceController;
            _displayFormatter = displayFormatter;
            FormDataService = formDataService;
        }

        private readonly CurrencyNameTable _currencies;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly StoreRepository _storeRepository;
        private readonly UriResolver _uriResolver;
        private readonly AppService _appService;
        private readonly UIInvoiceController _invoiceController;
        private readonly DisplayFormatter _displayFormatter;
        public FormDataService FormDataService { get; }

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
                AppId = appId,
                StoreId = store.Id,
                Description = settings.Description,
            });
        }

        [HttpPost("/")]
        [HttpPost("/apps/{appId}/pos/{viewType?}")]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [DomainMappingConstraint(PointOfSaleAppType.AppType)]
        [RateLimitsFilter(ZoneLimits.PublicInvoices, Scope = RateLimitsScope.RemoteAddress)]
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
            var app = await _appService.GetApp(appId, PointOfSaleAppType.AppType);

            // not allowing negative tips or discounts
            if (tip < 0 || discount < 0)
                return RedirectToAction(nameof(ViewPointOfSale), new { appId });

            if (string.IsNullOrEmpty(choiceKey) && amount <= 0)
                return RedirectToAction(nameof(ViewPointOfSale), new { appId });

            if (app == null)
                return NotFound();
            
            var settings = app.GetSettings<PointOfSaleSettings>();
            settings.DefaultView = settings.EnableShoppingCart ? PosViewType.Cart : settings.DefaultView;
            var currentView = viewType ?? settings.DefaultView;
            if (string.IsNullOrEmpty(choiceKey) && !settings.ShowCustomAmount &&
                currentView != PosViewType.Cart && currentView != PosViewType.Light)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId, viewType });
            }
            var jposData = TryParseJObject(posData);
            string title;
            decimal? price;
            Dictionary<string, InvoiceSupportedTransactionCurrency> paymentMethods = null;
            ViewPointOfSaleViewModel.Item choice = null;
            List<PosCartItem> cartItems = null;
            ViewPointOfSaleViewModel.Item[] choices = null;
            if (!string.IsNullOrEmpty(choiceKey))
            {
                choices = AppService.Parse(settings.Template, false);
                choice = choices.FirstOrDefault(c => c.Id == choiceKey);
                if (choice == null)
                    return NotFound();
                title = choice.Title;
                if (choice.PriceType == ViewPointOfSaleViewModel.ItemPriceType.Topup)
                {
                    price = null;
                }
                else
                {
                    price = choice.Price.Value;
                    if (amount > price)
                        price = amount;
                }

                if (choice.Inventory is <= 0)
                {
                    return RedirectToAction(nameof(ViewPointOfSale), new { appId });
                }

                if (choice?.PaymentMethods?.Any() is true)
                {
                    paymentMethods = choice?.PaymentMethods.ToDictionary(s => s,
                        s => new InvoiceSupportedTransactionCurrency() { Enabled = true });
                }
            }
            else
            {
                if (!settings.ShowCustomAmount && currentView != PosViewType.Cart && currentView != PosViewType.Light)
                    return NotFound();

                title = settings.Title;
                // if cart IS enabled and we detect posdata that matches the cart system's, check inventory for the items
                price = amount;
                if (AppService.TryParsePosCartItems(jposData, out cartItems))
                {
                    price = jposData.TryGetValue("amounts", out var amounts) && amounts is JArray { Count: > 0 } amountsArray
                        ? amountsArray.Values<decimal>().Sum()
                        : 0.0m;
                    choices = AppService.Parse(settings.Template, false);
                    foreach (var cartItem in cartItems)
                    {
                        var itemChoice = choices.FirstOrDefault(item => item.Id == cartItem.Id);
                        if (itemChoice == null)
                            return NotFound();

                        if (itemChoice.Inventory.HasValue)
                        {
                            switch (itemChoice.Inventory)
                            {
                                case <= 0:
                                    return RedirectToAction(nameof(ViewPointOfSale), new { appId });
                                case { } inventory when inventory < cartItem.Count:
                                    return RedirectToAction(nameof(ViewPointOfSale), new { appId });
                            }
                        }

                        var expectedCartItemPrice = itemChoice.PriceType != ViewPointOfSaleViewModel.ItemPriceType.Topup
                            ? itemChoice.Price ?? 0
                            : 0;
                        
                        if (cartItem.Price < expectedCartItemPrice)
                            cartItem.Price = expectedCartItemPrice;

                        price += cartItem.Price * cartItem.Count;
                    }
                    if (customAmount is { } c)
                        price += c;
                    if (discount is { } d)
                        price -= price * d/100.0m;
                    if (tip is { } t)
                        price += t;
                }
            }

            var store = await _appService.GetStore(app);
            var storeBlob = store.GetStoreBlob();
            var posFormId = settings.FormId;
            var formData = await FormDataService.GetForm(posFormId);

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
                            FormUrl = Url.Action(nameof(POSForm), "UIPointOfSale", new {appId, buyerEmail = email}),
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
                        form.Fields.Add(new Field
                        {
                            Name = $"{FormDataService.InvoiceParameterPrefix}amount",
                            Type = "hidden",
                            Value = price?.ToString(),
                            Constant = true
                        });
                    }
                    else
                    {
                        amtField.Value = price?.ToString();
                    }
                    formResponseJObject = FormDataService.GetValues(form);
                    
                    var invoiceRequest = FormDataService.GenerateInvoiceParametersFromForm(form);
                    if (invoiceRequest.Amount is not null)
                    {
                        price = invoiceRequest.Amount.Value;
                    }
                    break;
            }
            try
            {
                var invoice = await _invoiceController.CreateInvoiceCoreRaw(new CreateInvoiceRequest
                {
                    Amount = price,
                    Currency = settings.Currency,
                    Metadata = new InvoiceMetadata
                    {
                        ItemCode = choice?.Id,
                        ItemDesc = title,
                        BuyerEmail = email,
                        OrderId = orderId ?? AppService.GetRandomOrderId()
                    }.ToJObject(),
                    Checkout = new InvoiceDataBase.CheckoutOptions()
                    {
                        RedirectAutomatically = settings.RedirectAutomatically,
                        RedirectURL = !string.IsNullOrEmpty(redirectUrl) ? redirectUrl
                            : !string.IsNullOrEmpty(settings.RedirectUrl) ? settings.RedirectUrl
                            : Request.GetAbsoluteUri(Url.Action(nameof(ViewPointOfSale), "UIPointOfSale", new { appId, viewType })),
                        PaymentMethods = paymentMethods?.Where(p => p.Value.Enabled).Select(p => p.Key).ToArray()
                    },
                    AdditionalSearchTerms = new [] { AppService.GetAppSearchTerm(app) }
                }, store, HttpContext.Request.GetAbsoluteRoot(),
                    new List<string> { AppService.GetAppInternalTag(appId) },
                    cancellationToken, entity =>
                    {
                        entity.NotificationURLTemplate =
                            string.IsNullOrEmpty(notificationUrl) ? settings.NotificationUrl : notificationUrl;
                        entity.FullNotifications = true;
                        entity.ExtendedNotifications = true;
                        entity.Metadata.OrderUrl = Request.GetDisplayUrl();
                        entity.Metadata.PosData = jposData;
                        var receiptData = new JObject();
                        if (choice is not null)
                        {
                            var dict = new Dictionary<string, string> { { "Title", choice.Title } };
                            if (!string.IsNullOrEmpty(choice.Description)) dict["Description"] = choice.Description;
                            receiptData = JObject.FromObject(dict);
                        }
                        else if (jposData is not null)
                        {
                            var appPosData = jposData.ToObject<PosAppData>();
                            receiptData = new JObject();
                            if (cartItems is not null && choices is not null)
                            {
                                var posCartItems = cartItems.ToList();
                                var selectedChoices = choices
                                    .Where(item => posCartItems.Any(cartItem => cartItem.Id == item.Id))
                                    .ToDictionary(item => item.Id);
                                var cartData = new JObject();
                                foreach (PosCartItem cartItem in posCartItems)
                                {
                                    if (!selectedChoices.TryGetValue(cartItem.Id, out var selectedChoice)) continue;
                                    var singlePrice = _displayFormatter.Currency(cartItem.Price, settings.Currency, DisplayFormatter.CurrencyFormat.Symbol);
                                    var totalPrice = _displayFormatter.Currency(cartItem.Price * cartItem.Count, settings.Currency, DisplayFormatter.CurrencyFormat.Symbol);
                                    var ident = selectedChoice.Title ?? selectedChoice.Id;
                                    var key = selectedChoice.PriceType == ViewPointOfSaleViewModel.ItemPriceType.Fixed ? ident : $"{ident} ({singlePrice})";
                                    cartData.Add(key, $"{cartItem.Count} x {singlePrice} = {totalPrice}");
                                }

                                if (jposData.TryGetValue("amounts", out var amounts) && amounts is JArray { Count: > 0 } amountsArray)
                                {
                                    for (var i = 0; i < amountsArray.Count; i++)
                                    {
                                        cartData.Add($"Custom Amount {i+1}", _displayFormatter.Currency(amountsArray[i].ToObject<decimal>(), settings.Currency, DisplayFormatter.CurrencyFormat.Symbol));
                                    }
                                }
                                receiptData.Add("Cart", cartData);
                            }
                            receiptData.Add("Subtotal", _displayFormatter.Currency(appPosData.Subtotal, settings.Currency, DisplayFormatter.CurrencyFormat.Symbol));
                            if (appPosData.DiscountAmount > 0)
                            {
                                var discountFormatted = _displayFormatter.Currency(appPosData.DiscountAmount, settings.Currency, DisplayFormatter.CurrencyFormat.Symbol);
                                receiptData.Add("Discount", appPosData.DiscountPercentage > 0 ? $"{appPosData.DiscountPercentage}% = {discountFormatted}" : discountFormatted);
                            }
                            if (appPosData.Tip > 0)
                            {
                                var tipFormatted = _displayFormatter.Currency(appPosData.Tip, settings.Currency, DisplayFormatter.CurrencyFormat.Symbol);
                                receiptData.Add("Tip", appPosData.TipPercentage > 0 ? $"{appPosData.TipPercentage}% = {tipFormatted}" : tipFormatted);
                            }
                            receiptData.Add("Total", _displayFormatter.Currency(appPosData.Total, settings.Currency, DisplayFormatter.CurrencyFormat.Symbol));
                        }
                        entity.Metadata.SetAdditionalData("receiptData", receiptData);

                        if (formResponseJObject is null)
                            return;
                        var meta = entity.Metadata.ToJObject();
                        meta.Merge(formResponseJObject);
                        entity.Metadata = InvoiceMetadata.FromJObject(meta);
                    });
                if (price is 0 && storeBlob.ReceiptOptions?.Enabled is true)
                {
                    return RedirectToAction(nameof(UIInvoiceController.InvoiceReceipt), "UIInvoice", new { invoiceId = invoice.Id });
                }
                return RedirectToAction(nameof(UIInvoiceController.Checkout), "UIInvoice", new { invoiceId = invoice.Id });
            }
            catch (BitpayHttpException e)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Html = e.Message.Replace("\n", "<br />", StringComparison.OrdinalIgnoreCase),
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    AllowDismiss = true
                });
                return RedirectToAction(nameof(ViewPointOfSale), new { appId });
            }
        }

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
                        Request.GetAbsoluteUri(Url.Action(nameof(ViewPointOfSale), controller, new { appId, viewType }));
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
                    ["status"] = i.GetInvoiceState().Status.ToString(),
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
                Description = settings.Description,
                NotificationUrl = settings.NotificationUrl,
                RedirectUrl = settings.RedirectUrl,
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
                Description = vm.Description,
                RedirectAutomatically = string.IsNullOrEmpty(vm.RedirectAutomatically) ? null : bool.Parse(vm.RedirectAutomatically),
                FormId = vm.FormId
            };

            app.Name = vm.AppName;
            app.Archived = vm.Archived;
            app.SetSettings(settings);
            await _appService.UpdateOrCreateApp(app);
            TempData[WellKnownTempData.SuccessMessage] = "App updated";
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
            vm.StoreUsers = users.Select(u => (u.Id, u.Email, u.StoreRole.Role)).ToDictionary(u => u.Id, u => $"{u.Email} ({u.Role})");
        }
    }
}
