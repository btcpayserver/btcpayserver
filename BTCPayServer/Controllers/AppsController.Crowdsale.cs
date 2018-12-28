using System;
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
        }
        
        public class CrowdfundSettings
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public bool Enabled { get; set; }
        
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
            };
            return View(vm);
        }
        [HttpPost]
        [Route("{appId}/settings/crowdfund")]
        public async Task<IActionResult> UpdatePointOfSale(string appId, UpdateCrowdfundViewModel vm)
        {
            if (_AppsHelper.GetCurrencyData(vm.TargetCurrency, false) == null)
                ModelState.AddModelError(nameof(vm.TargetCurrency), "Invalid currency");
          
            var app = await GetOwnedApp(appId, AppType.Crowdfund);
            if (app == null)
                return NotFound();
            app.SetSettings(new CrowdfundSettings()
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
                Tagline = vm.Tagline
            });
            await UpdateAppSettings(app);
            _EventAggregator.Publish(new CrowdfundAppUpdated()
            {
                AppId = appId
            });
            StatusMessage = "App updated";
            return RedirectToAction(nameof(ListApps));
        }
    }
}
