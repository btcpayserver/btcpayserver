using System;
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
        public class CrowdfundAppUpdated
        {
            public string AppId { get; set; }
            public CrowdfundSettings Settings { get; set; }
            public string StoreId { get; set; }
        }
        
        public class CrowdfundSettings
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public bool Enabled { get; set; } = false;
        
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        
            public string TargetCurrency { get; set; }
            public decimal? TargetAmount { get; set; }
        
            public bool EnforceTargetAmount { get; set; }
            public string CustomCSSLink { get; set; }
            public string MainImageUrl { get; set; }
            public string NotificationUrl { get; set; }
            public string Tagline { get; set; }
            public string EmbeddedCSS { get; set; }
            public string PerksTemplate { get; set; }
            public bool DisqusEnabled { get; set; }= false;
            public bool SoundsEnabled { get; set; }= true;
            public string DisqusShortname { get; set; }
            public bool AnimationsEnabled { get; set; } = true;
            public bool UseInvoiceAmount { get; set; } = true;
            public int ResetEveryAmount { get; set; } = 1;
            public CrowdfundResetEvery ResetEvery { get; set; } = CrowdfundResetEvery.Never;
            public bool UseAllStoreInvoices { get; set; } = false;
        }
        
        
        [HttpGet]
        [Route("{appId}/settings/crowdfund")]
        public async Task<IActionResult> UpdateCrowdfund(string appId)
        {
            var app = await GetOwnedApp(appId, AppType.Crowdfund);
            if (app == null)
                return NotFound();
            var settings = app.GetSettings<CrowdfundSettings>();
            var vm = new UpdateCrowdfundViewModel()
            {
                Title = settings.Title,
                Enabled = settings.Enabled,
                EnforceTargetAmount = settings.EnforceTargetAmount,
                StartDate = settings.StartDate,
                TargetCurrency = settings.TargetCurrency,
                Description = settings.Description,
                MainImageUrl = settings.MainImageUrl,
                EmbeddedCSS = settings.EmbeddedCSS,
                EndDate = settings.EndDate,
                TargetAmount = settings.TargetAmount,
                CustomCSSLink = settings.CustomCSSLink,
                NotificationUrl = settings.NotificationUrl,
                Tagline = settings.Tagline,
                PerksTemplate = settings.PerksTemplate,
                DisqusEnabled = settings.DisqusEnabled,
                SoundsEnabled = settings.SoundsEnabled,
                DisqusShortname = settings.DisqusShortname,
                AnimationsEnabled = settings.AnimationsEnabled,
                UseInvoiceAmount = settings.UseInvoiceAmount,
                ResetEveryAmount = settings.ResetEveryAmount,
                ResetEvery = Enum.GetName(typeof(CrowdfundResetEvery), settings.ResetEvery),
                UseAllStoreInvoices = settings.UseAllStoreInvoices,
                AppId = appId
            };
            
                        if (HttpContext?.Request != null)
            {
                var appUrl = HttpContext.Request.GetAbsoluteRoot().WithTrailingSlash() + $"apps/{appId}/crowdfund";
                var encoder = HtmlEncoder.Default;
                    var  builder = new StringBuilder();
                    builder.AppendLine($"<form method=\"POST\" action=\"{encoder.Encode(appUrl)}\">");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"amount\" value=\"100\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"redirectToCheckout\" value=\"true\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"email\" value=\"customer@example.com\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"redirectUrl\" value=\"https://example.com/thanksyou\" />");
                    builder.AppendLine($"  <button type=\"submit\">Contribute now</button>");
                    builder.AppendLine($"</form>");
                    vm.Example1 = builder.ToString();
                
                try
                {
                    var items = _AppsHelper.Parse(settings.PerksTemplate, settings.TargetCurrency);
                    builder = new StringBuilder();
                    builder.AppendLine($"<form method=\"POST\" action=\"{encoder.Encode(appUrl)}\">");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"redirectToCheckout\" value=\"true\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"email\" value=\"customer@example.com\" />");
                    builder.AppendLine($"  <input type=\"hidden\" name=\"redirectUrl\" value=\"https://example.com/thanksyou\" />");
                    builder.AppendLine($"  <button type=\"submit\" name=\"choiceKey\" value=\"{items[0].Id}\">Buy now</button>");
                    builder.AppendLine($"</form>");
                    vm.Example2 = builder.ToString();
                }
                catch { }
            }

            vm.ExampleCallback = "{\n  \"id\":\"SkdsDghkdP3D3qkj7bLq3\",\n  \"url\":\"https://btcpay.example.com/invoice?id=SkdsDghkdP3D3qkj7bLq3\",\n  \"status\":\"paid\",\n  \"price\":10,\n  \"currency\":\"EUR\",\n  \"invoiceTime\":1520373130312,\n  \"expirationTime\":1520374030312,\n  \"currentTime\":1520373179327,\n  \"exceptionStatus\":false,\n  \"buyerFields\":{\n    \"buyerEmail\":\"customer@example.com\",\n    \"buyerNotify\":false\n  },\n  \"paymentSubtotals\": {\n    \"BTC\":114700\n  },\n  \"paymentTotals\": {\n    \"BTC\":118400\n  },\n  \"transactionCurrency\": \"BTC\",\n  \"amountPaid\": \"1025900\",\n  \"exchangeRates\": {\n    \"BTC\": {\n      \"EUR\": 8721.690715789999,\n      \"USD\": 10817.99\n    }\n  }\n}";
            
            return View(vm);
        }
        [HttpPost]
        [Route("{appId}/settings/crowdfund")]
        public async Task<IActionResult> UpdateCrowdfund(string appId, UpdateCrowdfundViewModel vm)
        {
            if (!string.IsNullOrEmpty( vm.TargetCurrency) && _AppsHelper.GetCurrencyData(vm.TargetCurrency, false) == null)
                ModelState.AddModelError(nameof(vm.TargetCurrency), "Invalid currency");
          
            try
            {
                _AppsHelper.Parse(vm.PerksTemplate, vm.TargetCurrency);
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.PerksTemplate), "Invalid template");
            }

            if (Enum.Parse<CrowdfundResetEvery>(vm.ResetEvery) != CrowdfundResetEvery.Never && !vm.StartDate.HasValue)
            {
                ModelState.AddModelError(nameof(vm.StartDate), "A start date is needed when the goal resets every X amount of time.");
            }

            if (Enum.Parse<CrowdfundResetEvery>(vm.ResetEvery) != CrowdfundResetEvery.Never && vm.ResetEveryAmount <= 0)
            {
                ModelState.AddModelError(nameof(vm.ResetEveryAmount), "You must reset the goal at a minimum of 1 ");
            }
            
            if (!ModelState.IsValid)
            {
                return View(vm);
            }
            
            
            var app = await GetOwnedApp(appId, AppType.Crowdfund);
            if (app == null)
                return NotFound();

            var newSettings = new CrowdfundSettings()
            {
                Title = vm.Title,
                Enabled = vm.Enabled,
                EnforceTargetAmount = vm.EnforceTargetAmount,
                StartDate = vm.StartDate,
                TargetCurrency = vm.TargetCurrency,
                Description = vm.Description,
                EndDate = vm.EndDate,
                TargetAmount = vm.TargetAmount,
                CustomCSSLink = vm.CustomCSSLink,
                MainImageUrl = vm.MainImageUrl,
                EmbeddedCSS = vm.EmbeddedCSS,
                NotificationUrl = vm.NotificationUrl,
                Tagline = vm.Tagline,
                PerksTemplate = vm.PerksTemplate,
                DisqusEnabled = vm.DisqusEnabled,
                SoundsEnabled = vm.SoundsEnabled,
                DisqusShortname = vm.DisqusShortname,
                AnimationsEnabled = vm.AnimationsEnabled,
                ResetEveryAmount = vm.ResetEveryAmount,
                ResetEvery = Enum.Parse<CrowdfundResetEvery>(vm.ResetEvery),
                UseInvoiceAmount = vm.UseInvoiceAmount,
                UseAllStoreInvoices = vm.UseAllStoreInvoices
            };
            
            app.SetSettings(newSettings);
            await UpdateAppSettings(app);
            _EventAggregator.Publish(new CrowdfundAppUpdated()
            {
                AppId = appId,
                StoreId = app.StoreDataId,
                Settings = newSettings
            });
            StatusMessage = "App updated";
            return RedirectToAction(nameof(ListApps));
        }
    }
}
