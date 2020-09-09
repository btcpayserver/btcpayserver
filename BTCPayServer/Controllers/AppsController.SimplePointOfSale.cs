using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class AppsController
    {
        public class SimplePointOfSaleSettings
        {
            public SimplePointOfSaleSettings()
            {
                Title = "Tea shop";
                Currency = "USD";
            }
            public string Title { get; set; }
            public string Currency { get; set; }
            public string CustomCSSLink { get; set; }
            public string EmbeddedCSS { get; set; }
            public string NotificationUrl { get; set; }
            public bool? RedirectAutomatically { get; set; }
        }

        [HttpGet]
        [Route("{appId}/settings/simple-pos")]
        public async Task<IActionResult> UpdateSimplePointOfSale(string appId)
        {
            var app = await GetOwnedApp(appId, AppType.SimplePointOfSale);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<SimplePointOfSaleSettings>();

            var vm = new UpdateSimplePointOfSaleViewModel()
            {
                Id = appId,
                StoreId = app.StoreDataId,
                Title = settings.Title,
                Currency = settings.Currency,
                CustomCSSLink = settings.CustomCSSLink,
                EmbeddedCSS = settings.EmbeddedCSS,
                NotificationUrl = settings.NotificationUrl,
                SearchTerm = $"storeid:{app.StoreDataId}",
                RedirectAutomatically = settings.RedirectAutomatically.HasValue ? settings.RedirectAutomatically.Value ? "true" : "false" : ""
            };
            if (HttpContext?.Request != null)
            {
                var appUrl = HttpContext.Request.GetAbsoluteRoot().WithTrailingSlash() + $"apps/{appId}/simple-pos";
                var encoder = HtmlEncoder.Default;
                var builder = new StringBuilder();
                builder.AppendLine($"<form method=\"POST\" action=\"{encoder.Encode(appUrl)}\">");
                builder.AppendLine($"  <input type=\"hidden\" name=\"email\" value=\"customer@example.com\" />");
                builder.AppendLine($"  <input type=\"hidden\" name=\"orderId\" value=\"CustomOrderId\" />");
                builder.AppendLine($"  <input type=\"hidden\" name=\"notificationUrl\" value=\"https://example.com/callbacks\" />");
                builder.AppendLine($"  <input type=\"hidden\" name=\"redirectUrl\" value=\"https://example.com/thanksyou\" />");
                builder.AppendLine($"  <button type=\"submit\">Buy now</button>");
                builder.AppendLine($"</form>");
                vm.Example2 = builder.ToString();
                vm.InvoiceUrl = appUrl + "invoices/SkdsDghkdP3D3qkj7bLq3";
            }

            vm.ExampleCallback = "{\n  \"id\":\"SkdsDghkdP3D3qkj7bLq3\",\n  \"url\":\"https://btcpay.example.com/invoice?id=SkdsDghkdP3D3qkj7bLq3\",\n  \"status\":\"paid\",\n  \"price\":10,\n  \"currency\":\"EUR\",\n  \"invoiceTime\":1520373130312,\n  \"expirationTime\":1520374030312,\n  \"currentTime\":1520373179327,\n  \"exceptionStatus\":false,\n  \"buyerFields\":{\n    \"buyerEmail\":\"customer@example.com\",\n    \"buyerNotify\":false\n  },\n  \"paymentSubtotals\": {\n    \"BTC\":114700\n  },\n  \"paymentTotals\": {\n    \"BTC\":118400\n  },\n  \"transactionCurrency\": \"BTC\",\n  \"amountPaid\": \"1025900\",\n  \"exchangeRates\": {\n    \"BTC\": {\n      \"EUR\": 8721.690715789999,\n      \"USD\": 10817.99\n    }\n  }\n}";
            return View(vm);
        }
        [HttpPost]
        [Route("{appId}/settings/simple-pos")]
        public async Task<IActionResult> UpdateSimplePointOfSale(string appId, UpdateSimplePointOfSaleViewModel vm)
        {
            if (_currencies.GetCurrencyData(vm.Currency, false) == null)
                ModelState.AddModelError(nameof(vm.Currency), "Invalid currency");

            if (!ModelState.IsValid)
            {
                return View(vm);
            }
            var app = await GetOwnedApp(appId, AppType.SimplePointOfSale);
            if (app == null)
                return NotFound();
            app.SetSettings(new SimplePointOfSaleSettings()
            {
                Title = vm.Title,
                Currency = vm.Currency.ToUpperInvariant(),
                CustomCSSLink = vm.CustomCSSLink,
                NotificationUrl = vm.NotificationUrl,
                EmbeddedCSS = vm.EmbeddedCSS,
                RedirectAutomatically = string.IsNullOrEmpty(vm.RedirectAutomatically) ? (bool?)null : bool.Parse(vm.RedirectAutomatically)
            });
            await _AppService.UpdateOrCreateApp(app);
            TempData[WellKnownTempData.SuccessMessage] = "App updated";
            return RedirectToAction(nameof(UpdateSimplePointOfSale), new { appId });
        }
    }
}
