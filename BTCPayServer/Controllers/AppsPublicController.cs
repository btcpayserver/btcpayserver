using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitpayClient;
using YamlDotNet.RepresentationModel;
using static BTCPayServer.Controllers.AppsController;

namespace BTCPayServer.Controllers
{
    public class AppsPublicController : Controller
    {
        public AppsPublicController(AppsHelper appsHelper, 
            InvoiceController invoiceController, 
            RateFetcher rateFetcher,
            BTCPayNetworkProvider btcPayNetworkProvider,
            InvoiceRepository invoiceRepository)
        {
            _AppsHelper = appsHelper;
            _InvoiceController = invoiceController;
            _rateFetcher = rateFetcher;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _invoiceRepository = invoiceRepository;
        }

        private AppsHelper _AppsHelper;
        private InvoiceController _InvoiceController;
        private readonly RateFetcher _rateFetcher;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly InvoiceRepository _invoiceRepository;

        [HttpGet]
        [Route("/apps/{appId}/pos")]
        [XFrameOptionsAttribute(null)]
        public async Task<IActionResult> ViewPointOfSale(string appId)
        {
            var app = await _AppsHelper.GetApp(appId, AppType.PointOfSale);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<PointOfSaleSettings>();

            var numberFormatInfo = _AppsHelper.Currencies.GetNumberFormatInfo(settings.Currency) ?? _AppsHelper.Currencies.GetNumberFormatInfo("USD");
            double step = Math.Pow(10, -(numberFormatInfo.CurrencyDecimalDigits));

            return View(new ViewPointOfSaleViewModel()
            {
                Title = settings.Title,
                Step = step.ToString(CultureInfo.InvariantCulture),
                EnableShoppingCart = settings.EnableShoppingCart,
                ShowCustomAmount = settings.ShowCustomAmount,
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
                Items = _AppsHelper.Parse(settings.Template, settings.Currency),
                ButtonText = settings.ButtonText,
                CustomButtonText = settings.CustomButtonText,
                CustomTipText = settings.CustomTipText,
                CustomCSSLink = settings.CustomCSSLink
            });
        }
        
        
        [HttpGet]
        [Route("/apps/{appId}/crowdfund")]
        [XFrameOptionsAttribute(null)]
        public async Task<IActionResult> ViewCrowdfund(string appId, string statusMessage)
        
        {
            var app = await _AppsHelper.GetApp(appId, AppType.Crowdfund, true);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<CrowdfundSettings>();
            var currency = _AppsHelper.GetCurrencyData(settings.TargetCurrency, false);
            
            return View(await CrowdfundHelper.GetInfo(app, _invoiceRepository, _rateFetcher, _btcPayNetworkProvider, statusMessage ));
        }

        [HttpPost]
        [Route("/apps/{appId}/crowdfund")]
        [XFrameOptionsAttribute(null)]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> ContributeToCrowdfund(string appId, ContributeToCrowdfund request)
        {
            var app = await _AppsHelper.GetApp(appId, AppType.Crowdfund, true);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<CrowdfundSettings>();
            var store = await _AppsHelper.GetStore(app);

            store.AdditionalClaims.Add(new Claim(Policies.CanCreateInvoice.Key, store.Id));
            var invoice = await _InvoiceController.CreateInvoiceCore(new Invoice()
            {
                OrderId = appId,
                Currency = settings.TargetCurrency,
                BuyerEmail = request.Email,
                Price = request.Amount,
                NotificationURL = settings.NotificationUrl,
                FullNotifications = true,
                ExtendedNotifications = true,
            }, store, HttpContext.Request.GetAbsoluteRoot());
            if (request.RedirectToCheckout)
            {
                return RedirectToAction(nameof(InvoiceController.Checkout), "Invoice",
                    new {invoiceId = invoice.Data.Id});
            }
            else
            {
                return Json(new
                {
                    InvoiceId = invoice.Data.Id
                });
            }
        }
        

        [HttpPost]
        [Route("/apps/{appId}/pos")]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> ViewPointOfSale(string appId,
                                                        decimal amount,
                                                        string email,
                                                        string orderId,
                                                        string notificationUrl,
                                                        string redirectUrl,
                                                        string choiceKey)
        {
            var app = await _AppsHelper.GetApp(appId, AppType.PointOfSale);
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
            if (!string.IsNullOrEmpty(choiceKey))
            {
                var choices = _AppsHelper.Parse(settings.Template, settings.Currency);
                var choice = choices.FirstOrDefault(c => c.Id == choiceKey);
                if (choice == null)
                    return NotFound();
                title = choice.Title;
                price = choice.Price.Value;
                if (amount > price)
                    price = amount;
            }
            else
            {
                if (!settings.ShowCustomAmount && !settings.EnableShoppingCart)
                    return NotFound();
                price = amount;
                title = settings.Title;
            }
            var store = await _AppsHelper.GetStore(app);
            store.AdditionalClaims.Add(new Claim(Policies.CanCreateInvoice.Key, store.Id));
            var invoice = await _InvoiceController.CreateInvoiceCore(new NBitpayClient.Invoice()
            {
                ItemCode = choiceKey ?? string.Empty,
                ItemDesc = title,
                Currency = settings.Currency,
                Price = price,
                BuyerEmail = email,
                OrderId = orderId,
                NotificationURL = notificationUrl,
                RedirectURL = redirectUrl,
                FullNotifications = true
            }, store, HttpContext.Request.GetAbsoluteRoot());
            return RedirectToAction(nameof(InvoiceController.Checkout), "Invoice", new { invoiceId = invoice.Data.Id });
        }
    }

    public class CrowdfundHelper
    {
        private static async Task<decimal> GetCurrentContributionAmount(InvoiceEntity[] invoices, string primaryCurrency,
            RateFetcher rateFetcher, RateRules rateRules)
        {
            decimal result = 0;
            
            var groupingByCurrency = invoices.GroupBy(entity => entity.ProductInformation.Currency);

            var ratesTask = rateFetcher.FetchRates(
                groupingByCurrency
                    .Select((entities) => new CurrencyPair(entities.Key, primaryCurrency))
                    .ToHashSet(), 
                rateRules);

            var finalTasks = new List<Task>();
            foreach (var rateTask in ratesTask)
            {
                finalTasks.Add(Task.Run(async () =>
                {
                    var tResult = await rateTask.Value;
                    var rate = tResult.BidAsk?.Bid;
                    if (rate == null) return;
                    var currencyGroup = groupingByCurrency.Single(entities => entities.Key == rateTask.Key.Left);
                    result += currencyGroup.Sum(entity => entity.ProductInformation.Price / rate.Value);
                }));
            }

            await Task.WhenAll(finalTasks);

            return result;

        }
        
        public static async Task<ViewCrowdfundViewModel> GetInfo(AppData appData, InvoiceRepository invoiceRepository,
            RateFetcher rateFetcher, BTCPayNetworkProvider btcPayNetworkProvider, string statusMessage= null)
        {
            var settings = appData.GetSettings<CrowdfundSettings>();
            var invoices = await GetPaidInvoicesForApp(appData, invoiceRepository);
            var rateRules = appData.StoreData.GetStoreBlob().GetRateRules(btcPayNetworkProvider);
            var currentAmount = await GetCurrentContributionAmount(
                invoices, 
                settings.TargetCurrency, rateFetcher, rateRules);
            var paidInvoices = invoices.Length;
            var active = (settings.StartDate == null || DateTime.UtcNow >= settings.StartDate) &&
                         (settings.EndDate == null || DateTime.UtcNow <= settings.EndDate) &&
                         (!settings.EnforceTargetAmount || settings.TargetAmount > currentAmount);

            return new ViewCrowdfundViewModel()
            {
                Title = settings.Title,
                Tagline = settings.Tagline,
                Description = settings.Description,
                CustomCSSLink = settings.CustomCSSLink,
                MainImageUrl = settings.MainImageUrl,
                StoreId = appData.StoreDataId,
                AppId = appData.Id,
                StartDate = settings.StartDate, 
                EndDate = settings.EndDate,
                TargetAmount = settings.TargetAmount,
                TargetCurrency = settings.TargetCurrency,
                EnforceTargetAmount = settings.EnforceTargetAmount,
                StatusMessage = statusMessage,
                Info = new ViewCrowdfundViewModel.CrowdfundInfo()
                {
                    TotalContributors = paidInvoices,
                    CurrentAmount = currentAmount,
                    Active = active,
                    DaysLeft = settings.EndDate.HasValue? (settings.EndDate - DateTime.UtcNow).Value.Days: (int?) null,
                    DaysLeftToStart = settings.StartDate.HasValue? (settings.StartDate - DateTime.UtcNow).Value.Days: (int?) null,
                    ShowProgress =active && settings.TargetAmount.HasValue,
                    ProgressPercentage =   currentAmount/ settings.TargetAmount * 100
                }
            };
        }

        private static async Task<InvoiceEntity[]> GetPaidInvoicesForApp(AppData appData, InvoiceRepository invoiceRepository)
        {
            return await  invoiceRepository.GetInvoices(new InvoiceQuery()
            {
                OrderId = appData.Id,
                Status = new string[]{ InvoiceState.ToString(InvoiceStatus.Complete)}
            });
        }
    }
    

    public class AppsHelper
    {
        ApplicationDbContextFactory _ContextFactory;
        CurrencyNameTable _Currencies;
        public CurrencyNameTable Currencies => _Currencies;
        public AppsHelper(ApplicationDbContextFactory contextFactory, CurrencyNameTable currencies)
        {
            _ContextFactory = contextFactory;
            _Currencies = currencies;

        }

        public async Task<AppData> GetApp(string appId, AppType appType, bool includeStore = false)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var query = ctx.Apps
                    .Where(us => us.Id == appId &&
                                 us.AppType == appType.ToString());

                if (includeStore)
                {
                    query = query.Include(data => data.StoreData);
                }
                return await query.FirstOrDefaultAsync();
            }
        }

        public async Task<StoreData> GetStore(AppData app)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.Stores.FirstOrDefaultAsync(s => s.Id == app.StoreDataId);
            }
        }


        public ViewPointOfSaleViewModel.Item[] Parse(string template, string currency)
        {
            if (string.IsNullOrWhiteSpace(template))
                return Array.Empty<ViewPointOfSaleViewModel.Item>();
            var input = new StringReader(template);
            YamlStream stream = new YamlStream();
            stream.Load(input);
            var root = (YamlMappingNode)stream.Documents[0].RootNode;
            return root
                .Children
                .Select(kv => new PosHolder { Key = (kv.Key as YamlScalarNode)?.Value, Value = kv.Value as YamlMappingNode })
                .Where(kv => kv.Value != null)
                .Select(c => new ViewPointOfSaleViewModel.Item()
                {
                    Description = c.GetDetailString("description"),
                    Id = c.Key,
                    Image = c.GetDetailString("image"),
                    Title = c.GetDetailString("title") ?? c.Key,
                    Price = c.GetDetail("price")
                             .Select(cc => new ViewPointOfSaleViewModel.Item.ItemPrice()
                             {
                                 Value = decimal.Parse(cc.Value.Value, CultureInfo.InvariantCulture),
                                 Formatted = FormatCurrency(cc.Value.Value, currency)
                             }).Single(),
                    Custom = c.GetDetailString("custom") == "true"
                })
                .ToArray();
        }

        private class PosHolder
        {
            public string Key { get; set; }
            public YamlMappingNode Value { get; set; }

            public IEnumerable<PosScalar> GetDetail(string field)
            {
                var res = Value.Children
                                 .Where(kv => kv.Value != null)
                                 .Select(kv => new PosScalar { Key = (kv.Key as YamlScalarNode)?.Value, Value = kv.Value as YamlScalarNode })
                                 .Where(cc => cc.Key == field);
                return res;
            }

            public string GetDetailString(string field)
            {
                return GetDetail(field).FirstOrDefault()?.Value?.Value;
            }
        }
        private class PosScalar
        {
            public string Key { get; set; }
            public YamlScalarNode Value { get; set; }
        }

        public string FormatCurrency(string price, string currency)
        {
            return decimal.Parse(price, CultureInfo.InvariantCulture).ToString("C", _Currencies.GetCurrencyProvider(currency));
        }

        public CurrencyData GetCurrencyData(string currency, bool useFallback)
        {
            return _Currencies.GetCurrencyData(currency, useFallback);
        }
        public async Task<AppData> GetAppDataIfOwner(string userId, string appId, AppType? type = null)
        {
            if (userId == null || appId == null)
                return null;
            using (var ctx = _ContextFactory.CreateContext())
            {
                var app = await ctx.UserStore
                                .Where(us => us.ApplicationUserId == userId && us.Role == StoreRoles.Owner)
                                .SelectMany(us => us.StoreData.Apps.Where(a => a.Id == appId))
                   .FirstOrDefaultAsync();
                if (app == null)
                    return null;
                if (type != null && type.Value.ToString() != app.AppType)
                    return null;
                return app;
            }
        }
    }
}
