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
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Cors;

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
            var vm = new UpdatePointOfSaleViewModel()
            {
                Title = settings.Title,
                ShowCustomAmount = settings.ShowCustomAmount,
                Currency = settings.Currency,
                Template = settings.Template
            };
            if (HttpContext?.Request != null)
            {
                var appUrl = HttpContext.Request.GetAbsoluteRoot().WithTrailingSlash() + $"apps/{appId}/pos";
                var encoder = HtmlEncoder.Default;
                if (settings.ShowCustomAmount)
                {
                    StringBuilder builder = new StringBuilder();
                    builder.AppendLine($"<form method=\"POST\" action=\"{encoder.Encode(appUrl)}\">");
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
                    var items = Parse(settings.Template, settings.Currency);
                    var builder = new StringBuilder();
                    builder.AppendLine($"<form method=\"POST\" action=\"{encoder.Encode(appUrl)}\">");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"email\" value=\"customer@example.com\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"orderId\" value=\"CustomOrderId\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"notificationUrl\" value=\"https://example.com/callbacks\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"redirectUrl\" value=\"https://example.com/thanksyou\" />");
                    builder.AppendLine($"  <button type=\"submit\" name=\"choiceKey\" value=\"{items[0].Id}\">Buy now</button>");
                    builder.AppendLine($"</form>");
                    vm.Example2 = builder.ToString();
                }
                catch { }
                vm.InvoiceUrl = appUrl + "invoices/SkdsDghkdP3D3qkj7bLq3";
            }

            vm.ExampleCallback = "{\n  \"id\":\"SkdsDghkdP3D3qkj7bLq3\",\n  \"url\":\"https://btcpay.example.com/invoice?id=SkdsDghkdP3D3qkj7bLq3\",\n  \"status\":\"paid\",\n  \"price\":10,\n  \"currency\":\"EUR\",\n  \"invoiceTime\":1520373130312,\n  \"expirationTime\":1520374030312,\n  \"currentTime\":1520373179327,\n  \"exceptionStatus\":false,\n  \"buyerFields\":{\n    \"buyerEmail\":\"customer@example.com\",\n    \"buyerNotify\":false\n  },\n  \"paymentSubtotals\": {\n    \"BTC\":114700\n  },\n  \"paymentTotals\": {\n    \"BTC\":118400\n  },\n  \"transactionCurrency\": \"BTC\",\n  \"amountPaid\": \"1025900\",\n  \"exchangeRates\": {\n    \"BTC\": {\n      \"EUR\": 8721.690715789999,\n      \"USD\": 10817.99\n    }\n  }\n}";
            return View(vm);
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
            return RedirectToAction(nameof(ListApps));
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
                                .Where(us => us.Id == appId &&
                                             us.AppType == appType.ToString())
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
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> ViewPointOfSale(string appId,
                                                        decimal amount,
                                                        string email,
                                                        string orderId,
                                                        string notificationUrl,
                                                        string redirectUrl,
                                                        string choiceKey)
        {
            var app = await GetApp(appId, AppType.PointOfSale);
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
                var choices = Parse(settings.Template, settings.Currency);
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
            var store = await GetStore(app);
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
