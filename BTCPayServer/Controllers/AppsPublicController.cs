using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;
using static BTCPayServer.Controllers.AppsController;

namespace BTCPayServer.Controllers
{
    public class AppsPublicController : Controller
    {
        public AppsPublicController(AppService appService,
            BTCPayServerOptions btcPayServerOptions,
            InvoiceController invoiceController,
            UserManager<ApplicationUser> userManager)
        {
            _AppService = appService;
            _BtcPayServerOptions = btcPayServerOptions;
            _InvoiceController = invoiceController;
            _UserManager = userManager;
        }

        private readonly AppService _AppService;
        private readonly BTCPayServerOptions _BtcPayServerOptions;
        private readonly InvoiceController _InvoiceController;
        private readonly UserManager<ApplicationUser> _UserManager;

        [HttpGet("/apps/{appId}")]
        public async Task<IActionResult> RedirectToApp(string appId)
        {
           
            switch ((await _AppService.GetApp(appId, null)).AppType)
            {
                case nameof(AppType.Crowdfund):
                    return RedirectToAction("ViewCrowdfund", new {appId});
                
                case nameof(AppType.PointOfSale):
                    return RedirectToAction("ViewPointOfSale", new {appId});
            }

            return NotFound();
        }
        
        [HttpGet]
        [Route("/")]
        [Route("/apps/{appId}/pos/{viewType?}")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [DomainMappingConstraint(AppType.PointOfSale)]
        public async Task<IActionResult> ViewPointOfSale(string appId, PosViewType? viewType = null)
        {
            var app = await _AppService.GetApp(appId, AppType.PointOfSale);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<PointOfSaleSettings>();
            var numberFormatInfo = _AppService.Currencies.GetNumberFormatInfo(settings.Currency) ?? _AppService.Currencies.GetNumberFormatInfo("USD");
            double step = Math.Pow(10, -(numberFormatInfo.CurrencyDecimalDigits));
            viewType ??= settings.EnableShoppingCart ? PosViewType.Cart : settings.DefaultView;
            var store = await _AppService.GetStore(app);
            var storeBlob = store.GetStoreBlob();

            return View("PointOfSale/" + viewType, new ViewPointOfSaleViewModel()
            {
                Title = settings.Title,
                Step = step.ToString(CultureInfo.InvariantCulture),
                ViewType = (PosViewType)viewType,
                ShowCustomAmount = settings.ShowCustomAmount,
                ShowDiscount = settings.ShowDiscount,
                EnableTips = settings.EnableTips,
                CurrencyCode = settings.Currency,
                CurrencySymbol = numberFormatInfo.CurrencySymbol,
                CurrencyInfo = new ViewPointOfSaleViewModel.CurrencyInfoData()
                {
                    CurrencySymbol = string.IsNullOrEmpty(numberFormatInfo.CurrencySymbol) ? settings.Currency : numberFormatInfo.CurrencySymbol,
                    Divisibility = numberFormatInfo.CurrencyDecimalDigits,
                    DecimalSeparator = numberFormatInfo.CurrencyDecimalSeparator,
                    ThousandSeparator = numberFormatInfo.NumberGroupSeparator,
                    Prefixed = new[] { 0, 2 }.Contains(numberFormatInfo.CurrencyPositivePattern),
                    SymbolSpace = new[] { 2, 3 }.Contains(numberFormatInfo.CurrencyPositivePattern)
                },
                Items = _AppService.Parse(settings.Template, settings.Currency),
                ButtonText = settings.ButtonText,
                CustomButtonText = settings.CustomButtonText,
                CustomTipText = settings.CustomTipText,
                CustomTipPercentages = settings.CustomTipPercentages,
                CustomCSSLink = settings.CustomCSSLink,
                CustomLogoLink = storeBlob.CustomLogo,
                AppId = appId,
                Description = settings.Description,
                EmbeddedCSS = settings.EmbeddedCSS
            });
        }

        [HttpPost]
        [Route("/")]
        [Route("/apps/{appId}/pos/{viewType?}")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [DomainMappingConstraint(AppType.PointOfSale)]
        public async Task<IActionResult> ViewPointOfSale(string appId,
                                                        PosViewType viewType,
                                                        [ModelBinder(typeof(InvariantDecimalModelBinder))] decimal amount,
                                                        string email,
                                                        string orderId,
                                                        string notificationUrl,
                                                        string redirectUrl,
                                                        string choiceKey,
                                                        string posData = null, CancellationToken cancellationToken = default)
        {
            var app = await _AppService.GetApp(appId, AppType.PointOfSale);
            if (string.IsNullOrEmpty(choiceKey) && amount <= 0)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId = appId });
            }
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<PointOfSaleSettings>();
            settings.DefaultView = settings.EnableShoppingCart ? PosViewType.Cart : settings.DefaultView;
            if (string.IsNullOrEmpty(choiceKey) && !settings.ShowCustomAmount && settings.DefaultView != PosViewType.Cart)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId = appId, viewType = viewType });
            }
            string title = null;
            var price = 0.0m;
            Dictionary<string, InvoiceSupportedTransactionCurrency> paymentMethods = null;
            ViewPointOfSaleViewModel.Item choice = null;
            if (!string.IsNullOrEmpty(choiceKey))
            {
                var choices = _AppService.Parse(settings.Template, settings.Currency);
                choice = choices.FirstOrDefault(c => c.Id == choiceKey);
                if (choice == null)
                    return NotFound();
                title = choice.Title;
                price = choice.Price.Value;
                if (amount > price)
                    price = amount;

                if (choice.Inventory.HasValue)
                {
                    if (choice.Inventory <= 0)
                    {
                        return RedirectToAction(nameof(ViewPointOfSale), new { appId = appId });
                    }
                }

                if (choice?.PaymentMethods?.Any() is true)
                {
                    paymentMethods = choice?.PaymentMethods.ToDictionary(s => s,
                        s => new InvoiceSupportedTransactionCurrency() { Enabled = true });
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

                    var choices = _AppService.Parse(settings.Template, settings.Currency);
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
            var store = await _AppService.GetStore(app);
            try
            {
                var invoice = await _InvoiceController.CreateInvoiceCore(new BitpayCreateInvoiceRequest()
                {
                    ItemCode = choice?.Id,
                    ItemDesc = title,
                    Currency = settings.Currency,
                    Price = price,
                    BuyerEmail = email,
                    OrderId = orderId,
                    NotificationURL =
                            string.IsNullOrEmpty(notificationUrl) ? settings.NotificationUrl : notificationUrl,
                    RedirectURL = !string.IsNullOrEmpty(redirectUrl) ? redirectUrl
                                : !string.IsNullOrEmpty(settings.RedirectUrl) ? settings.RedirectUrl
                                : Request.GetDisplayUrl(),
                    FullNotifications = true,
                    ExtendedNotifications = true,
                    PosData = string.IsNullOrEmpty(posData) ? null : posData,
                    RedirectAutomatically = settings.RedirectAutomatically,
                    SupportedTransactionCurrencies = paymentMethods,
                }, store, HttpContext.Request.GetAbsoluteRoot(),
                    new List<string>() { AppService.GetAppInternalTag(appId) },
                    cancellationToken);
                return RedirectToAction(nameof(InvoiceController.Checkout), "Invoice", new { invoiceId = invoice.Data.Id });
            }
            catch (BitpayHttpException e)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Html = e.Message.Replace("\n", "<br />", StringComparison.OrdinalIgnoreCase),
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    AllowDismiss = true
                });
                return RedirectToAction(nameof(ViewPointOfSale), new { appId = appId });
            }
        }

        [HttpGet]
        [Route("/")]
        [Route("/apps/{appId}/crowdfund")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [DomainMappingConstraintAttribute(AppType.Crowdfund)]
        public async Task<IActionResult> ViewCrowdfund(string appId, string statusMessage)
        {
            var app = await _AppService.GetApp(appId, AppType.Crowdfund, true);

            if (app == null)
                return NotFound();
            var settings = app.GetSettings<CrowdfundSettings>();

            var isAdmin = await _AppService.GetAppDataIfOwner(GetUserId(), appId, AppType.Crowdfund) != null;

            var hasEnoughSettingsToLoad = !string.IsNullOrEmpty(settings.TargetCurrency);
            if (!hasEnoughSettingsToLoad)
            {
                if (!isAdmin)
                    return NotFound();

                return NotFound("A Target Currency must be set for this app in order to be loadable.");
            }
            var appInfo = (ViewCrowdfundViewModel)(await _AppService.GetAppInfo(appId));
            appInfo.HubPath = AppHub.GetHubPath(this.Request);
            if (settings.Enabled)
                return View(appInfo);
            if (!isAdmin)
                return NotFound();

            return View(appInfo);
        }

        [HttpPost]
        [Route("/")]
        [Route("/apps/{appId}/crowdfund")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [DomainMappingConstraintAttribute(AppType.Crowdfund)]
        public async Task<IActionResult> ContributeToCrowdfund(string appId, ContributeToCrowdfund request, CancellationToken cancellationToken)
        {
            if (request.Amount <= 0)
            {
                return NotFound("Please provide an amount greater than 0");
            }
            var app = await _AppService.GetApp(appId, AppType.Crowdfund, true);

            if (app == null)
                return NotFound();
            var settings = app.GetSettings<CrowdfundSettings>();


            var isAdmin = await _AppService.GetAppDataIfOwner(GetUserId(), appId, AppType.Crowdfund) != null;

            if (!settings.Enabled && !isAdmin)
            {
                return NotFound("Crowdfund is not currently active");
            }

            var info = (ViewCrowdfundViewModel)await _AppService.GetAppInfo(appId);
            info.HubPath = AppHub.GetHubPath(this.Request);
            if (!isAdmin &&
                ((settings.StartDate.HasValue && DateTime.Now < settings.StartDate) ||
                 (settings.EndDate.HasValue && DateTime.Now > settings.EndDate) ||
                 (settings.EnforceTargetAmount &&
                  (info.Info.PendingProgressPercentage.GetValueOrDefault(0) +
                   info.Info.ProgressPercentage.GetValueOrDefault(0)) >= 100)))
            {
                return NotFound("Crowdfund is not currently active");
            }

            var store = await _AppService.GetStore(app);
            var title = settings.Title;
            var price = request.Amount;
            Dictionary<string, InvoiceSupportedTransactionCurrency> paymentMethods = null;
            ViewPointOfSaleViewModel.Item choice = null;
            if (!string.IsNullOrEmpty(request.ChoiceKey))
            {
                var choices = _AppService.Parse(settings.PerksTemplate, settings.TargetCurrency);
                choice = choices.FirstOrDefault(c => c.Id == request.ChoiceKey);
                if (choice == null)
                    return NotFound("Incorrect option provided");
                title = choice.Title;
                price = choice.Price.Value;
                if (request.Amount > price)
                    price = request.Amount;


                if (choice.Inventory.HasValue)
                {
                    if (choice.Inventory <= 0)
                    {
                        return NotFound("Option was out of stock");
                    }
                }


                if (choice?.PaymentMethods?.Any() is true)
                {
                    paymentMethods = choice?.PaymentMethods.ToDictionary(s => s,
                        s => new InvoiceSupportedTransactionCurrency() { Enabled = true });
                }
            }

            if (!isAdmin && (settings.EnforceTargetAmount && info.TargetAmount.HasValue && price >
                             (info.TargetAmount - (info.Info.CurrentAmount + info.Info.CurrentPendingAmount))))
            {
                return NotFound("Contribution Amount is more than is currently allowed.");
            }

            try
            {
                var invoice = await _InvoiceController.CreateInvoiceCore(new BitpayCreateInvoiceRequest()
                {
                    OrderId = AppService.GetCrowdfundOrderId(appId),
                    Currency = settings.TargetCurrency,
                    ItemCode = request.ChoiceKey ?? string.Empty,
                    ItemDesc = title,
                    BuyerEmail = request.Email,
                    Price = price,
                    NotificationURL = settings.NotificationUrl,
                    FullNotifications = true,
                    ExtendedNotifications = true,
                    SupportedTransactionCurrencies = paymentMethods,
                    RedirectURL = request.RedirectUrl ??
                                     new Uri(new Uri(new Uri(HttpContext.Request.GetAbsoluteRoot()), _BtcPayServerOptions.RootPath), $"apps/{appId}/crowdfund").ToString()
                }, store, HttpContext.Request.GetAbsoluteRoot(),
                    new List<string> { AppService.GetAppInternalTag(appId) },
                    cancellationToken: cancellationToken);
                if (request.RedirectToCheckout)
                {
                    return RedirectToAction(nameof(InvoiceController.Checkout), "Invoice",
                        new { invoiceId = invoice.Data.Id });
                }
                else
                {
                    return Ok(invoice.Data.Id);
                }
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
    }
}
