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
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
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
using NicolasDorier.RateLimits;
using PosViewType = BTCPayServer.Services.Apps.PosViewType;

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
            UIInvoiceController invoiceController)
        {
            _currencies = currencies;
            _appService = appService;
            _storeRepository = storeRepository;
            _invoiceController = invoiceController;
        }

        private readonly CurrencyNameTable _currencies;
        private readonly StoreRepository _storeRepository;
        private readonly AppService _appService;
        private readonly UIInvoiceController _invoiceController;
        
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
                                                        PosViewType viewType,
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
            if (string.IsNullOrEmpty(choiceKey) && !settings.ShowCustomAmount && settings.DefaultView != PosViewType.Cart)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId, viewType });
            }
            string title = null;
            decimal? price = null;
            string[] paymentMethods = null;
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

                if (choice.Inventory.HasValue)
                {
                    if (choice.Inventory <= 0)
                    {
                        return RedirectToAction(nameof(ViewPointOfSale), new { appId = appId });
                    }
                }

                if (choice?.PaymentMethods?.Any() is true)
                {
                    paymentMethods = choice.PaymentMethods;
                }
            }
            else
            {
                if (!settings.ShowCustomAmount && settings.DefaultView != PosViewType.Cart)
                    return NotFound();
                price = amount;
                title = settings.Title;

                //if cart IS enabled and we detect posdata that matches the cart system's, check inventory for the items
                if (!string.IsNullOrEmpty(posData) &&
                    settings.DefaultView == PosViewType.Cart &&
                    AppService.TryParsePosCartItems(posData, out var cartItems))
                {

                    var choices = _appService.GetPOSItems(settings.Template, settings.Currency);
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
                    }
                }
            }
            var store = await _appService.GetStore(app);
            try
            {
                var invoice = await _invoiceController.CreateInvoiceCoreRaw(new CreateInvoiceRequest()
                    {
                        Currency = settings.Currency,
                        Amount = price,
                        Type = price is null ? InvoiceType.TopUp : InvoiceType.Standard,
                        Checkout = new InvoiceDataBase.CheckoutOptions()
                        {
                            RedirectAutomatically = settings.RedirectAutomatically,
                            PaymentMethods = paymentMethods,
                            RedirectURL = !string.IsNullOrEmpty(redirectUrl) ? redirectUrl
                                : !string.IsNullOrEmpty(settings.RedirectUrl) ? settings.RedirectUrl
                                : Request.GetDisplayUrl(),
                            RequiresRefundEmail = requiresRefundEmail == RequiresRefundEmail.InheritFromStore
                                ? null
                                : requiresRefundEmail == RequiresRefundEmail.On,
                        },
                        Metadata = new InvoiceMetadata()
                        {
                            BuyerEmail = email,
                            ItemCode = choice?.Id,
                            ItemDesc = title,
                            OrderId = orderId ?? AppService.GetAppOrderId(app),
                            OrderUrl = Request.GetDisplayUrl(),
                            PosData = string.IsNullOrEmpty(posData) ? null : posData,
                        }.ToJObject()
                    }, store, HttpContext.Request.GetAbsoluteRoot(),
                    new List<string>() { AppService.GetAppInternalTag(appId) }, cancellationToken);
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
                RedirectUrl = settings.RedirectUrl,
                SearchTerm = app.TagAllInvoices ? $"storeid:{app.StoreDataId}" : $"orderid:{AppService.GetAppOrderId(app)}",
                RedirectAutomatically = settings.RedirectAutomatically.HasValue ? settings.RedirectAutomatically.Value ? "true" : "false" : "",
                RequiresRefundEmail = settings.RequiresRefundEmail
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

            app.Name = vm.AppName;
            app.SetSettings(new PointOfSaleSettings
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
                RedirectUrl = vm.RedirectUrl,
                Description = vm.Description,
                EmbeddedCSS = vm.EmbeddedCSS,
                RedirectAutomatically = string.IsNullOrEmpty(vm.RedirectAutomatically) ? (bool?)null : bool.Parse(vm.RedirectAutomatically),
                RequiresRefundEmail = vm.RequiresRefundEmail,
            });
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
        
        private AppData GetCurrentApp() => HttpContext.GetAppData();
    }
}
