using System;
using System.Threading.Tasks;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class AppsController
    {
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
                EndDate = settings.EndDate,
                TargetAmount = settings.TargetAmount,
                CustomCSSLink = settings.CustomCSSLink
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
                CustomCSSLink = vm.CustomCSSLink
            });
            await UpdateAppSettings(app);
            StatusMessage = "App updated";
            return RedirectToAction(nameof(ListApps));
        }
    }
}
