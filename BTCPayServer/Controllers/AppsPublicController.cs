using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Crowdfund;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Hubs;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Ganss.XSS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using YamlDotNet.RepresentationModel;
using static BTCPayServer.Controllers.AppsController;

namespace BTCPayServer.Controllers
{
    public class AppsPublicController : Controller
    {
        public AppsPublicController(AppsHelper appsHelper, 
            InvoiceController invoiceController, 
            CrowdfundHubStreamer crowdfundHubStreamer, UserManager<ApplicationUser> userManager)
        {
            _AppsHelper = appsHelper;
            _InvoiceController = invoiceController;
            _CrowdfundHubStreamer = crowdfundHubStreamer;
            _UserManager = userManager;
        }

        private AppsHelper _AppsHelper;
        private InvoiceController _InvoiceController;
        private readonly CrowdfundHubStreamer _CrowdfundHubStreamer;
        private readonly UserManager<ApplicationUser> _UserManager;

        [HttpGet]
        [Route("/apps/{appId}/pos")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
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
                CustomTipPercentages = settings.CustomTipPercentages,
                CustomCSSLink = settings.CustomCSSLink,
                AppId = appId
            });
        }
        
        
        [HttpGet]
        [Route("/apps/{appId}/crowdfund")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        public async Task<IActionResult> ViewCrowdfund(string appId, string statusMessage)
        
        {
            var app = await _AppsHelper.GetApp(appId, AppType.Crowdfund, true);
            
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<CrowdfundSettings>();
            
            var isAdmin = await _AppsHelper.GetAppDataIfOwner(GetUserId(), appId, AppType.Crowdfund) != null;
            
            var hasEnoughSettingsToLoad = !string.IsNullOrEmpty(settings.TargetCurrency );
            if (!hasEnoughSettingsToLoad)
            {
                if(!isAdmin)
                    return NotFound();

                return NotFound("A Target Currency must be set for this app in order to be loadable.");
            }
            if (settings.Enabled) return View(await _CrowdfundHubStreamer.GetCrowdfundInfo(appId));
            if(!isAdmin)
                return NotFound();

            return View(await _CrowdfundHubStreamer.GetCrowdfundInfo(appId));
        }

        [HttpPost]
        [Route("/apps/{appId}/crowdfund")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> ContributeToCrowdfund(string appId, ContributeToCrowdfund request)
        {
            var app = await _AppsHelper.GetApp(appId, AppType.Crowdfund, true);
            
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<CrowdfundSettings>();
            
           
            var isAdmin = await _AppsHelper.GetAppDataIfOwner(GetUserId(), appId, AppType.Crowdfund) != null;
 
            if (!settings.Enabled)
            {
                if(!isAdmin)
                    return NotFound("Crowdfund is not currently active");
            }

            var info = await _CrowdfundHubStreamer.GetCrowdfundInfo(appId);
            
            if(!isAdmin && 
               
               ((settings.StartDate.HasValue && DateTime.Now  < settings.StartDate) || 
                (settings.EndDate.HasValue && DateTime.Now  > settings.EndDate) || 
                (settings.EnforceTargetAmount && 
                    (info.Info.PendingProgressPercentage.GetValueOrDefault(0) + 
                     info.Info.ProgressPercentage.GetValueOrDefault(0)) >= 100)))
            {
                return NotFound("Crowdfund is not currently active");
            }

            var store = await _AppsHelper.GetStore(app);
            var title =  settings.Title;
            var price = request.Amount;
            ViewPointOfSaleViewModel.Item choice = null;
            if (!string.IsNullOrEmpty(request.ChoiceKey))
            {
                var choices = _AppsHelper.Parse(settings.PerksTemplate, settings.TargetCurrency);
                choice = choices.FirstOrDefault(c => c.Id == request.ChoiceKey);
                if (choice == null)
                    return NotFound("Incorrect option provided");
                title = choice.Title;
                price = choice.Price.Value;
                if (request.Amount > price)
                    price = request.Amount;
            }

            if (!isAdmin && (settings.EnforceTargetAmount && info.TargetAmount.HasValue && price >
                (info.TargetAmount - (info.Info.CurrentAmount + info.Info.CurrentPendingAmount))))
            {
                return NotFound("Contribution Amount is more than is currently allowed.");
            }
            
            store.AdditionalClaims.Add(new Claim(Policies.CanCreateInvoice.Key, store.Id));
            try
            {
                var invoice = await _InvoiceController.CreateInvoiceCore(new Invoice()
                {
                    OrderId = $"{CrowdfundHubStreamer.CrowdfundInvoiceOrderIdPrefix}{appId}",
                    Currency = settings.TargetCurrency,
                    ItemCode = request.ChoiceKey ?? string.Empty,
                    ItemDesc = title,
                    BuyerEmail = request.Email,
                    Price = price,
                    NotificationURL = settings.NotificationUrl,
                    FullNotifications = true,
                    ExtendedNotifications = true,
                    RedirectURL = request.RedirectUrl ?? Request.GetDisplayUrl(),
                
                
                }, store, HttpContext.Request.GetAbsoluteRoot());
                if (request.RedirectToCheckout)
                {
                    return RedirectToAction(nameof(InvoiceController.Checkout), "Invoice",
                        new {invoiceId = invoice.Data.Id});
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
        

        [HttpPost]
        [Route("/apps/{appId}/pos")]
        [XFrameOptionsAttribute(XFrameOptionsAttribute.XFrameOptions.AllowAll)]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> ViewPointOfSale(string appId,
                                                        decimal amount,
                                                        string email,
                                                        string orderId,
                                                        string notificationUrl,
                                                        string redirectUrl,
                                                        string choiceKey,
                                                        string posData = null)
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
            ViewPointOfSaleViewModel.Item choice = null;
            if (!string.IsNullOrEmpty(choiceKey))
            {
                var choices = _AppsHelper.Parse(settings.Template, settings.Currency);
                choice = choices.FirstOrDefault(c => c.Id == choiceKey);
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
                ItemCode = choice?.Id,
                ItemDesc = title,
                Currency = settings.Currency,
                Price = price,
                BuyerEmail = email,
                OrderId = orderId,
                NotificationURL = notificationUrl,
                RedirectURL = redirectUrl  ?? Request.GetDisplayUrl(),
                FullNotifications = true,
                PosData = string.IsNullOrEmpty(posData) ? null : posData
            }, store, HttpContext.Request.GetAbsoluteRoot());
            return RedirectToAction(nameof(InvoiceController.Checkout), "Invoice", new { invoiceId = invoice.Data.Id });
        }
        
        
        private string GetUserId()
        {
            return _UserManager.GetUserId(User);
        }
    }

    public class AppsHelper
    {
        ApplicationDbContextFactory _ContextFactory;
        CurrencyNameTable _Currencies;
        private HtmlSanitizer _HtmlSanitizer;
        public CurrencyNameTable Currencies => _Currencies;
        public AppsHelper(ApplicationDbContextFactory contextFactory, CurrencyNameTable currencies)
        {
            _ContextFactory = contextFactory;
            _Currencies = currencies;
            ConfigureSanitizer();
        }
        
        private void ConfigureSanitizer()
        {
            
            _HtmlSanitizer = new HtmlSanitizer();


            _HtmlSanitizer.RemovingAtRule += (sender, args) =>
            {
                Debug.WriteLine("");
                
            };
            _HtmlSanitizer.RemovingTag += (sender, args) =>
            {
                Debug.WriteLine("");
                if (args.Tag.TagName.Equals("img", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (!args.Tag.ClassList.Contains("img-fluid"))
                    {
                        args.Tag.ClassList.Add("img-fluid");
                    }

                    args.Cancel = true;
                }
            };
            
            _HtmlSanitizer.RemovingAttribute += (sender, args) =>
            {
                if (args.Tag.TagName.Equals("img",StringComparison.InvariantCultureIgnoreCase) &&  
                    args.Attribute.Name.Equals( "src", StringComparison.InvariantCultureIgnoreCase) && 
                    args.Reason == RemoveReason.NotAllowedUrlValue)
                {
                    args.Cancel = true;
                }
                Debug.WriteLine("");
                
            };
            _HtmlSanitizer.RemovingStyle += (sender, args) => { args.Cancel = true; };
            _HtmlSanitizer.AllowedAttributes.Add("class");
            _HtmlSanitizer.AllowedTags.Add("iframe");
            _HtmlSanitizer.AllowedTags.Remove("img");
            _HtmlSanitizer.AllowedAttributes.Add("webkitallowfullscreen");
            _HtmlSanitizer.AllowedAttributes.Add("allowfullscreen");
        }

        public string Sanitize(string raw)
        {
            return _HtmlSanitizer.Sanitize(raw);
        }
        
        public async Task<StoreData[]> GetOwnedStores(string userId)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.UserStore
                    .Where(us => us.ApplicationUserId == userId && us.Role == StoreRoles.Owner)
                    .Select(u => u.StoreData)
                    .ToArrayAsync();
            }
        }

        public async Task<bool> DeleteApp(AppData appData)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                ctx.Apps.Add(appData);
                ctx.Entry<AppData>(appData).State = EntityState.Deleted;
                return await ctx.SaveChangesAsync() == 1;
            }
        }

        public async Task<ListAppsViewModel.ListAppViewModel[]> GetAllApps(string userId, bool allowNoUser = false)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.UserStore
                    .Where(us => (allowNoUser && string.IsNullOrEmpty(userId)  ) || us.ApplicationUserId == userId)
                    .Join(ctx.Apps, us => us.StoreDataId, app => app.StoreDataId,
                        (us, app) =>
                            new ListAppsViewModel.ListAppViewModel()
                            {
                                IsOwner = us.Role == StoreRoles.Owner,
                                StoreId = us.StoreDataId,
                                StoreName = us.StoreData.StoreName,
                                AppName = app.Name,
                                AppType = app.AppType,
                                Id = app.Id
                            })
                    .ToArrayAsync();
            }
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
                    Description = Sanitize(c.GetDetailString("description")),
                    Id = c.Key,
                    Image = Sanitize(c.GetDetailString("image")),
                    Title = Sanitize(c.GetDetailString("title") ?? c.Key),
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
