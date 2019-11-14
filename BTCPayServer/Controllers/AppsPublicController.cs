using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

        [HttpGet]
        [Route("/apps/{appId}/pos")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        public async Task<IActionResult> ViewPointOfSale(string appId)
        {
            var app = await _AppService.GetApp(appId, AppType.PointOfSale);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<PointOfSaleSettings>();

            var numberFormatInfo = _AppService.Currencies.GetNumberFormatInfo(settings.Currency) ?? _AppService.Currencies.GetNumberFormatInfo("USD");
            double step = Math.Pow(10, -(numberFormatInfo.CurrencyDecimalDigits));

            return View(new ViewPointOfSaleViewModel()
            {
                Title = settings.Title,
                Step = step.ToString(CultureInfo.InvariantCulture),
                EnableShoppingCart = settings.EnableShoppingCart,
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
                AppId = appId,
                Description = settings.Description,
                EmbeddedCSS = settings.EmbeddedCSS
            });
        }

        [HttpPost]
        [Route("/apps/{appId}/pos")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> ViewPointOfSale(string appId,
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
            if (string.IsNullOrEmpty(choiceKey) && !settings.ShowCustomAmount && !settings.EnableShoppingCart)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId = appId });
            }
            string title = null;
            var price = 0.0m;
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
            }
            else
            {
                if (!settings.ShowCustomAmount && !settings.EnableShoppingCart)
                    return NotFound();
                price = amount;
                title = settings.Title;
                
                //if cart IS enabled and we detect posdata that matches the cart system's, check inventory for the items
                if (!string.IsNullOrEmpty(posData) && 
                    settings.EnableShoppingCart && 
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
                                    return RedirectToAction(nameof(ViewPointOfSale), new {appId});
                                case int inventory when inventory < cartItem.Value:
                                    return RedirectToAction(nameof(ViewPointOfSale), new {appId});
                            }
                        }
                    }
                }
            }
            var store = await _AppService.GetStore(app);
            try
            {
                var invoice = await _InvoiceController.CreateInvoiceCore(new CreateInvoiceRequest()
                {
                    ItemCode = choice?.Id,
                    ItemDesc = title,
                    Currency = settings.Currency,
                    Price = price,
                    BuyerEmail = email,
                    OrderId = orderId,
                    NotificationURL =
                            string.IsNullOrEmpty(notificationUrl) ? settings.NotificationUrl : notificationUrl,
                    NotificationEmail = settings.NotificationEmail,
                    RedirectURL = redirectUrl ?? Request.GetDisplayUrl(),
                    FullNotifications = true,
                    ExtendedNotifications = true,
                    PosData = string.IsNullOrEmpty(posData) ? null : posData,
                    RedirectAutomatically = settings.RedirectAutomatically,
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
        [Route("/apps/{appId}/crowdfund")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
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
        [Route("/apps/{appId}/crowdfund")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> ContributeToCrowdfund(string appId, ContributeToCrowdfund request, CancellationToken cancellationToken)
        {
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
            }

            if (!isAdmin && (settings.EnforceTargetAmount && info.TargetAmount.HasValue && price >
                             (info.TargetAmount - (info.Info.CurrentAmount + info.Info.CurrentPendingAmount))))
            {
                return NotFound("Contribution Amount is more than is currently allowed.");
            }

            try
            {
                var invoice = await _InvoiceController.CreateInvoiceCore(new CreateInvoiceRequest()
                    {
                        OrderId = AppService.GetCrowdfundOrderId(appId),
                        Currency = settings.TargetCurrency,
                        ItemCode = request.ChoiceKey ?? string.Empty,
                        ItemDesc = title,
                        BuyerEmail = request.Email,
                        Price = price,
                        NotificationURL = settings.NotificationUrl,
                        NotificationEmail = settings.NotificationEmail,
                        FullNotifications = true,
                        ExtendedNotifications = true,
                        RedirectURL = request.RedirectUrl ?? 
                                     new Uri(new Uri( new Uri(HttpContext.Request.GetAbsoluteRoot()),  _BtcPayServerOptions.RootPath), $"apps/{appId}/crowdfund").ToString()
                    }, store, HttpContext.Request.GetAbsoluteRoot(),
                    new List<string> {AppService.GetAppInternalTag(appId)},
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
