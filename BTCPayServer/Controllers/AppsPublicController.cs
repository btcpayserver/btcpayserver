using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YamlDotNet.RepresentationModel;
using static BTCPayServer.Controllers.AppsController;

namespace BTCPayServer.Controllers
{
    public class AppsPublicController : Controller
    {
        public AppsPublicController(AppsHelper appsHelper, InvoiceController invoiceController)
        {
            _AppsHelper = appsHelper;
            _InvoiceController = invoiceController;
        }

        private AppsHelper _AppsHelper;
        private InvoiceController _InvoiceController;

        [HttpGet]
        [Route("/apps/{appId}/pos")]
        public async Task<IActionResult> ViewPointOfSale(string appId)
        {
            var app = await _AppsHelper.GetApp(appId, AppType.PointOfSale);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<PointOfSaleSettings>();
            var currency = _AppsHelper.GetCurrencyData(settings.Currency, false);
            double step = currency == null ? 1 : Math.Pow(10, -(currency.Divisibility));

            return View(new ViewPointOfSaleViewModel()
            {
                Title = settings.Title,
                Step = step.ToString(CultureInfo.InvariantCulture),
                ShowCustomAmount = settings.ShowCustomAmount,
                Items = _AppsHelper.Parse(settings.Template, settings.Currency)
            });
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
            if (string.IsNullOrEmpty(choiceKey) && !settings.ShowCustomAmount)
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
            }
            else
            {
                if (!settings.ShowCustomAmount)
                    return NotFound();
                price = amount;
                title = settings.Title;
            }
            var store = await _AppsHelper.GetStore(app);
            store.AdditionalClaims.Add(new Claim(Policies.CanCreateInvoice.Key, store.Id));
            var invoice = await _InvoiceController.CreateInvoiceCore(new NBitpayClient.Invoice()
            {
                ItemDesc = title,
                Currency = settings.Currency,
                Price = price,
                BuyerEmail = email,
                OrderId = orderId,
                NotificationURL = notificationUrl,
                RedirectURL = redirectUrl,
                FullNotifications = true
            }, store, HttpContext.Request.GetAbsoluteRoot());
            return Redirect(invoice.Data.Url);
        }
    }


    public class AppsHelper
    {
        ApplicationDbContextFactory _ContextFactory;
        CurrencyNameTable _Currencies;

        public AppsHelper(ApplicationDbContextFactory contextFactory, CurrencyNameTable currencies)
        {
            _ContextFactory = contextFactory;
            _Currencies = currencies;

        }

        public async Task<AppData> GetApp(string appId, AppType appType)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.Apps
                                .Where(us => us.Id == appId &&
                                             us.AppType == appType.ToString())
                                .FirstOrDefaultAsync();
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
                .Select(kv => new { Key = (kv.Key as YamlScalarNode)?.Value, Value = kv.Value as YamlMappingNode })
                .Where(kv => kv.Value != null)
                .Select(c => new ViewPointOfSaleViewModel.Item()
                {
                    Description = c.Value.Children
                             .Select(kv => new { Key = (kv.Key as YamlScalarNode)?.Value, Value = kv.Value as YamlScalarNode })
                             .Where(kv => kv.Value != null)
                             .Where(cc => cc.Key == "description")
                             .FirstOrDefault()?.Value?.Value,
                    Id = c.Key,
                    Image = c.Value.Children
                             .Select(kv => new { Key = (kv.Key as YamlScalarNode)?.Value, Value = kv.Value as YamlScalarNode })
                             .Where(kv => kv.Value != null)
                             .Where(cc => cc.Key == "image")
                             .FirstOrDefault()?.Value?.Value,
                    Title = c.Value.Children
                             .Select(kv => new { Key = (kv.Key as YamlScalarNode)?.Value, Value = kv.Value as YamlScalarNode })
                             .Where(kv => kv.Value != null)
                             .Where(cc => cc.Key == "title")
                             .FirstOrDefault()?.Value?.Value ?? c.Key,
                    Price = c.Value.Children
                             .Select(kv => new { Key = (kv.Key as YamlScalarNode)?.Value, Value = kv.Value as YamlScalarNode })
                             .Where(kv => kv.Value != null)
                             .Where(cc => cc.Key == "price")
                             .Select(cc => new ViewPointOfSaleViewModel.Item.ItemPrice()
                             {
                                 Value = decimal.Parse(cc.Value.Value, CultureInfo.InvariantCulture),
                                 Formatted = FormatCurrency(cc.Value.Value, currency)
                             })
                             .Single()
                })
                .ToArray();
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
