using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
                    "  title: Green Tea # title is optional, defaults to the keys\n" +
                    "  description: Lovely, fresh and tender, Meng Ding Gan Lu is grown in the lush Meng Ding Mountains of the southwestern province of Sichuan # description is optional, defalts to none\n" +
                    "  image: https://cdn.pixabay.com/photo/2015/03/26/11/03/green-tea-692339__480.jpg # image is optional, defaults to none\n\n" +
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
                    var items = _AppsHelper.Parse(settings.Template, settings.Currency);
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
            if (_AppsHelper.GetCurrencyData(vm.Currency, false) == null)
                ModelState.AddModelError(nameof(vm.Currency), "Invalid currency");
            try
            {
                _AppsHelper.Parse(vm.Template, vm.Currency);
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
