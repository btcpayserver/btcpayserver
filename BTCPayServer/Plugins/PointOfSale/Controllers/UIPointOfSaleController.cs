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
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Forms;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using NicolasDorier.RateLimits;

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
            UIInvoiceController invoiceController,
            FormComponentProviders formProviders)
        {
            _currencies = currencies;
            _appService = appService;
            _storeRepository = storeRepository;
            _invoiceController = invoiceController;
            FormProviders = formProviders;
        }

        private readonly CurrencyNameTable _currencies;
        private readonly StoreRepository _storeRepository;
        private readonly AppService _appService;
        private readonly UIInvoiceController _invoiceController;

        public FormComponentProviders FormProviders { get; }

        [HttpGet("/")]
        [HttpGet("/apps/{appId}/pos/{viewType?}")]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [DomainMappingConstraint(AppType.PointOfSale)]
        public async Task<IActionResult> ViewPointOfSale(string appId, PosViewType? viewType = null)
        {
            var app = await _appService.GetApp(appId, AppType.PointOfSale);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<PointOfSaleSettings>();
            var numberFormatInfo = _appService.Currencies.GetNumberFormatInfo(settings.Currency) ?? 
                                   _appService.Currencies.GetNumberFormatInfo("USD");
            double step = Math.Pow(10, -numberFormatInfo.CurrencyDecimalDigits);
            viewType ??= settings.EnableShoppingCart ? PosViewType.Cart : settings.DefaultView;
            var store = await _appService.GetStore(app);
            var storeBlob = store.GetStoreBlob();

            return View($"PointOfSale/Public/{viewType}", new ViewPointOfSaleViewModel
            {
                Title = settings.Title,
                Step = step.ToString(CultureInfo.InvariantCulture),
                ViewType = (PosViewType)viewType,
                ShowCustomAmount = settings.ShowCustomAmount,
                ShowDiscount = settings.ShowDiscount,
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
                Items = _appService.GetPOSItems(settings.Template, settings.Currency),
                ButtonText = settings.ButtonText,
                CustomButtonText = settings.CustomButtonText,
                CustomTipText = settings.CustomTipText,
                CustomTipPercentages = settings.CustomTipPercentages,
                CustomCSSLink = settings.CustomCSSLink,
                CustomLogoLink = storeBlob.CustomLogo,
                AppId = appId,
                StoreId = store.Id,
                Description = settings.Description,
                EmbeddedCSS = settings.EmbeddedCSS,
                RequiresRefundEmail = settings.RequiresRefundEmail
            });
        }

        [HttpPost("/")]
        [HttpPost("/apps/{appId}/pos/{viewType?}")]
        [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [DomainMappingConstraint(AppType.PointOfSale)]
        [RateLimitsFilter(ZoneLimits.PublicInvoices, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> ViewPointOfSale(string appId,
                                                        PosViewType? viewType,
                                                        [ModelBinder(typeof(InvariantDecimalModelBinder))] decimal? amount,
                                                        string email,
                                                        string orderId,
                                                        string notificationUrl,
                                                        string redirectUrl,
                                                        string choiceKey,
                                                        string posData = null,
                                                        RequiresRefundEmail requiresRefundEmail = RequiresRefundEmail.InheritFromStore,
                                                        CancellationToken cancellationToken = default)
        {
            var app = await _appService.GetApp(appId, AppType.PointOfSale);
            if (string.IsNullOrEmpty(choiceKey) && amount <= 0)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId });
            }
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
            string title;
            decimal? price;
            Dictionary<string, InvoiceSupportedTransactionCurrency> paymentMethods = null;
            ViewPointOfSaleViewModel.Item choice = null;
            if (!string.IsNullOrEmpty(choiceKey))
            {
                var choices = _appService.GetPOSItems(settings.Template, settings.Currency);
                choice = choices.FirstOrDefault(c => c.Id == choiceKey);
                if (choice == null)
                    return NotFound();
                title = choice.Title;
                if (choice.Price.Type == ViewPointOfSaleViewModel.Item.ItemPrice.ItemPriceType.Topup)
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
                
                price = amount;
                title = settings.Title;

                //if cart IS enabled and we detect posdata that matches the cart system's, check inventory for the items
                if (!string.IsNullOrEmpty(posData) && currentView == PosViewType.Cart &&
                    AppService.TryParsePosCartItems(posData, out var cartItems))
                {
                    var choices = _appService.GetPOSItems(settings.Template, settings.Currency);
                    var expectedMinimumAmount = 0m;
                    foreach (var cartItem in cartItems)
                    {
                        var itemChoice = choices.FirstOrDefault(c => c.Id == cartItem.Key);
                        if (itemChoice == null)
                            return NotFound();

                        if (itemChoice.Inventory.HasValue)
                        {
                            switch (itemChoice.Inventory)
                            {
                                case int i when i <= 0:
                                    return RedirectToAction(nameof(ViewPointOfSale), new { appId });
                                case int inventory when inventory < cartItem.Value:
                                    return RedirectToAction(nameof(ViewPointOfSale), new { appId });
                            }
                        }

                        decimal expectedCartItemPrice = 0;
                        if (itemChoice.Price.Type != ViewPointOfSaleViewModel.Item.ItemPrice.ItemPriceType.Topup)
                        {
                            expectedCartItemPrice = itemChoice.Price.Value ?? 0;
                        }

                        expectedMinimumAmount += expectedCartItemPrice * cartItem.Value;
                    }

                    if (expectedMinimumAmount > amount)
                    {
                        return RedirectToAction(nameof(ViewPointOfSale), new { appId });
                    }
                }
            }

            var store = await _appService.GetStore(app);
            var posFormId = settings.FormId;

            var formConfig = posFormId is null ? null : Forms.UIFormsController.GetFormData(posFormId)?.Config;
            JObject formResponse = null;
            switch (formConfig)
            {
                case null:
                case { } when !this.Request.HasFormContentType:
                    break;
                default:
                    var formData = Form.Parse(formConfig);
                    formData.ApplyValuesFromForm(this.Request.Form);

                    if (FormProviders.Validate(formData, ModelState))
                    {
                        formResponse = JObject.FromObject(formData.GetValues());
                        break;
                    }
                    
                    var query = new QueryBuilder(Request.Query);
                    foreach (var keyValuePair in Request.Form)
                    {
                        query.Add(keyValuePair.Key, keyValuePair.Value.ToArray());
                    }
                    
                    // GET or empty form data case: Redirect to form
                    return View("PostRedirect", new PostRedirectViewModel
                    {
                        AspController = "UIForms",
                        AspAction = "ViewPublicForm",
                        RouteParameters =
                        {
                            { "formId", posFormId }
                        },
                        FormParameters =
                        {
                            { "redirectUrl", Request.GetCurrentUrl() + query }
                        }
                    });
            }
            try
            {
                var invoice = await _invoiceController.CreateInvoiceCore(new BitpayCreateInvoiceRequest
                {
                    ItemCode = choice?.Id,
                    ItemDesc = title,
                    Currency = settings.Currency,
                    Price = price,
                    BuyerEmail = email,
                    OrderId = orderId ?? AppService.GetAppOrderId(app),
                    NotificationURL =
                            string.IsNullOrEmpty(notificationUrl) ? settings.NotificationUrl : notificationUrl,
                    RedirectURL =  !string.IsNullOrEmpty(redirectUrl) ? redirectUrl
                        : !string.IsNullOrEmpty(settings.RedirectUrl) ? settings.RedirectUrl
                        : Request.GetDisplayUrl(),
                    FullNotifications = true,
                    ExtendedNotifications = true,
                    PosData = string.IsNullOrEmpty(posData) ? null : posData,
                    RedirectAutomatically = settings.RedirectAutomatically,
                    SupportedTransactionCurrencies = paymentMethods,
                    RequiresRefundEmail = requiresRefundEmail == RequiresRefundEmail.InheritFromStore
                        ? store.GetStoreBlob().RequiresRefundEmail
                        : requiresRefundEmail == RequiresRefundEmail.On,
                }, store, HttpContext.Request.GetAbsoluteRoot(),
                    new List<string> { AppService.GetAppInternalTag(appId) },
                    cancellationToken, (entity) =>
                    {
                        entity.Metadata.OrderUrl = Request.GetDisplayUrl();
                        
                        if (formResponse is not null)
                        {
                            var meta = entity.Metadata.ToJObject();
                            meta.Merge(formResponse);
                            entity.Metadata = InvoiceMetadata.FromJObject(meta);
                        }
                    } );
                return RedirectToAction(nameof(UIInvoiceController.Checkout), "UIInvoice", new { invoiceId = invoice.Data.Id });
            }
            catch (BitpayHttpException e)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Html = e.Message.Replace("\n", "<br />", StringComparison.OrdinalIgnoreCase),
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    AllowDismiss = true
                });
                return RedirectToAction(nameof(ViewPointOfSale), new { appId = appId });
            }
        }
        
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("{appId}/settings/pos")]
        public async Task<IActionResult> UpdatePointOfSale(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            var storeBlob = GetCurrentStore().GetStoreBlob();
            var settings = app.GetSettings<PointOfSaleSettings>();
            settings.DefaultView = settings.EnableShoppingCart ? PosViewType.Cart : settings.DefaultView;
            settings.EnableShoppingCart = false;

            var vm = new UpdatePointOfSaleViewModel
            {
                Id = appId,
                StoreId = app.StoreDataId,
                StoreName = app.StoreData?.StoreName,
                StoreDefaultCurrency = await GetStoreDefaultCurrentIfEmpty(app.StoreDataId, settings.Currency),
                AppName = app.Name,
                Title = settings.Title,
                DefaultView = settings.DefaultView,
                ShowCustomAmount = settings.ShowCustomAmount,
                ShowDiscount = settings.ShowDiscount,
                EnableTips = settings.EnableTips,
                Currency = settings.Currency,
                Template = settings.Template,
                ButtonText = settings.ButtonText ?? PointOfSaleSettings.BUTTON_TEXT_DEF,
                CustomButtonText = settings.CustomButtonText ?? PointOfSaleSettings.CUSTOM_BUTTON_TEXT_DEF,
                CustomTipText = settings.CustomTipText ?? PointOfSaleSettings.CUSTOM_TIP_TEXT_DEF,
                CustomTipPercentages = settings.CustomTipPercentages != null ? string.Join(",", settings.CustomTipPercentages) : string.Join(",", PointOfSaleSettings.CUSTOM_TIP_PERCENTAGES_DEF),
                CustomCSSLink = settings.CustomCSSLink,
                EmbeddedCSS = settings.EmbeddedCSS,
                Description = settings.Description,
                NotificationUrl = settings.NotificationUrl,
                RedirectUrl = settings.RedirectUrl,
                SearchTerm = app.TagAllInvoices ? $"storeid:{app.StoreDataId}" : $"orderid:{AppService.GetAppOrderId(app)}",
                RedirectAutomatically = settings.RedirectAutomatically.HasValue ? settings.RedirectAutomatically.Value ? "true" : "false" : "",
                RequiresRefundEmail = settings.RequiresRefundEmail,
                FormId = settings.FormId
            };
            if (HttpContext?.Request != null)
            {
                var appUrl = HttpContext.Request.GetAbsoluteUri($"/apps/{appId}/pos");
                var encoder = HtmlEncoder.Default;
                if (settings.ShowCustomAmount)
                {
                    StringBuilder builder = new StringBuilder();
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
                    var items = _appService.Parse(settings.Template, settings.Currency);
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
            return View("PointOfSale/UpdatePointOfSale", vm);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpPost("{appId}/settings/pos")]
        public async Task<IActionResult> UpdatePointOfSale(string appId, UpdatePointOfSaleViewModel vm)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            if (!ModelState.IsValid)
                return View("PointOfSale/UpdatePointOfSale", vm);

            vm.Currency = await GetStoreDefaultCurrentIfEmpty(app.StoreDataId, vm.Currency);
            if (_currencies.GetCurrencyData(vm.Currency, false) == null)
                ModelState.AddModelError(nameof(vm.Currency), "Invalid currency");
            try
            {
                vm.Template = _appService.SerializeTemplate(_appService.Parse(vm.Template, vm.Currency));
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.Template), "Invalid template");
            }
            if (!ModelState.IsValid)
            {
                return View("PointOfSale/UpdatePointOfSale", vm);
            }

            var storeBlob = GetCurrentStore().GetStoreBlob();
            var settings = new PointOfSaleSettings
            {
                Title = vm.Title,
                DefaultView = vm.DefaultView,
                ShowCustomAmount = vm.ShowCustomAmount,
                ShowDiscount = vm.ShowDiscount,
                EnableTips = vm.EnableTips,
                Currency = vm.Currency,
                Template = vm.Template,
                ButtonText = vm.ButtonText,
                CustomButtonText = vm.CustomButtonText,
                CustomTipText = vm.CustomTipText,
                CustomTipPercentages = ListSplit(vm.CustomTipPercentages),
                CustomCSSLink = vm.CustomCSSLink,
                NotificationUrl = vm.NotificationUrl,
                RedirectUrl = vm.RedirectUrl,
                Description = vm.Description,
                EmbeddedCSS = vm.EmbeddedCSS,
                RedirectAutomatically =
                    string.IsNullOrEmpty(vm.RedirectAutomatically) ? (bool?)null : bool.Parse(vm.RedirectAutomatically),
                RequiresRefundEmail = vm.RequiresRefundEmail
            };

            settings.FormId = vm.FormId;
            app.Name = vm.AppName;
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
    }
}
