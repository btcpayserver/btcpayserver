using System;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin.DataEncoders;
using NBitcoin;
using BTCPayServer.Services.Apps;
using Newtonsoft.Json;
using YamlDotNet.RepresentationModel;
using System.IO;
using BTCPayServer.Services.Rates;
using System.Globalization;

namespace BTCPayServer.Controllers
{
    public partial class AppsController
    {
        public class PointOfSaleSettings
        {
            public PointOfSaleSettings()
            {
                Title = "My awesome Point of Sale";
                Currency = "USD";
                Template =
                    "tea:\n" +
                    "  price: 0.02\n" +
                    "  title: Green Tea # title is optional, defaults to the keys\n\n" +
                    "coffee:\n" +
                    "  price: 1\n\n" +
                    "bamba:\n" +
                    "  price: 3\n\n" +
                    "beer:\n" +
                    "  price: 7\n\n" +
                    "hat:\n" +
                    "  price: 15\n\n" +
                    "tshirt:\n" +
                    "  price: 25";
                ShowCustomAmount = true;
            }
            public string Title { get; set; }
            public string Currency { get; set; }
            public string Template { get; set; }
            public bool ShowCustomAmount { get; set; }
        }

        [HttpGet]
        [Route("{appId}/settings/pos")]
        public async Task<IActionResult> UpdatePointOfSale(string appId)
        {
            var app = await GetOwnedApp(appId, AppType.PointOfSale);
            if (app == null)
                return NotFound();

            var settings = app.GetSettings<PointOfSaleSettings>();
            return View(new UpdatePointOfSaleViewModel() { Title = settings.Title, ShowCustomAmount = settings.ShowCustomAmount, Currency = settings.Currency, Template = settings.Template });
        }
        [HttpPost]
        [Route("{appId}/settings/pos")]
        public async Task<IActionResult> UpdatePointOfSale(string appId, UpdatePointOfSaleViewModel vm)
        {
            if (_Currencies.GetCurrencyData(vm.Currency, false) == null)
                ModelState.AddModelError(nameof(vm.Currency), "Invalid currency");
            try
            {
                Parse(vm.Template, vm.Currency);
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.Template), "Invalid template");
            }
            if (!ModelState.IsValid)
            {
                return View(vm);
            }
            var app = await GetOwnedApp(appId, AppType.PointOfSale);
            if (app == null)
                return NotFound();
            app.SetSettings(new PointOfSaleSettings()
            {
                Title = vm.Title,
                ShowCustomAmount = vm.ShowCustomAmount,
                Currency = vm.Currency.ToUpperInvariant(),
                Template = vm.Template
            });
            await UpdateAppSettings(app);
            StatusMessage = "App updated";
            return RedirectToAction(nameof(UpdatePointOfSale));
        }

        [HttpGet]
        [Route("{appId}/pos")]
        public async Task<IActionResult> ViewPointOfSale(string appId)
        {
            var app = await GetApp(appId, AppType.PointOfSale);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<PointOfSaleSettings>();
            var currency = _Currencies.GetCurrencyData(settings.Currency, false);
            double step = currency == null ? 1 : Math.Pow(10, -(currency.Divisibility));
            return View(new ViewPointOfSaleViewModel()
            {
                Title = settings.Title,
                Step = step.ToString(CultureInfo.InvariantCulture),
                ShowCustomAmount = settings.ShowCustomAmount,
                Items = Parse(settings.Template, settings.Currency)
            });
        }

        private async Task<AppData> GetApp(string appId, AppType appType)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.Apps
                                .Where(us => us.Id == appId && us.AppType == appType.ToString())
                                .FirstOrDefaultAsync();
            }
        }

        private ViewPointOfSaleViewModel.Item[] Parse(string template, string currency)
        {
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
                    Id = c.Key,
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

        string FormatCurrency(string price, string currency)
        {
            return decimal.Parse(price, CultureInfo.InvariantCulture).ToString("C", _Currencies.GetCurrencyProvider(currency));
        }

        [HttpPost]
        [Route("{appId}/pos")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ViewPointOfSale(string appId, decimal amount, string choiceKey)
        {
            var app = await GetApp(appId, AppType.PointOfSale);
            if (string.IsNullOrEmpty(choiceKey) && amount <= 0)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId = appId });
            }
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<PointOfSaleSettings>();
            if(string.IsNullOrEmpty(choiceKey) && !settings.ShowCustomAmount)
            {
                return RedirectToAction(nameof(ViewPointOfSale), new { appId = appId });
            }
            string title = null;
            var price = 0.0m;
            if (!string.IsNullOrEmpty(choiceKey))
            {
                var choices = Parse(settings.Template, settings.Currency);
                var choice = choices.FirstOrDefault(c => c.Id == choiceKey);
                if (choice == null)
                    return NotFound();
                title = choice.Title;
                price = choice.Price.Value;
            }
            else
            {
                price = amount;
                title = settings.Title;
            }

            var store = await GetStore(app);
            var invoice = await _InvoiceController.CreateInvoiceCore(new NBitpayClient.Invoice()
            {
                ItemDesc = title,
                Currency = settings.Currency,
                Price = price,
            }, store, HttpContext.Request.GetAbsoluteRoot());
            return Redirect(invoice.Data.Url);
        }

        private async Task<StoreData> GetStore(AppData app)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.Stores.FirstOrDefaultAsync(s => s.Id == app.StoreDataId);
            }
        }

        private async Task UpdateAppSettings(AppData app)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                ctx.Apps.Add(app);
                ctx.Entry<AppData>(app).State = EntityState.Modified;
                ctx.Entry<AppData>(app).Property(a => a.Settings).IsModified = true;
                await ctx.SaveChangesAsync();
            }
        }
    }
}
