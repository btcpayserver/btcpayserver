using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class UIAppsController
    {
        public class AppUpdated
        {
            public string AppId { get; set; }
            public object Settings { get; set; }
            public string StoreId { get; set; }
            public override string ToString()
            {
                return string.Empty;
            }
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        [HttpGet("{appId}/settings/crowdfund")]
        public async Task<IActionResult> UpdateCrowdfund(string appId)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            var settings = app.GetSettings<CrowdfundSettings>();
            var resetEvery = Enum.GetName(typeof(CrowdfundResetEvery), settings.ResetEvery);
            var vm = new UpdateCrowdfundViewModel
            {
                Title = settings.Title,
                StoreId = app.StoreDataId,
                StoreName = app.StoreData?.StoreName,
                StoreDefaultCurrency = await GetStoreDefaultCurrentIfEmpty(app.StoreDataId, settings.TargetCurrency),
                AppName = app.Name,
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
                ResetEveryAmount = settings.ResetEveryAmount,
                ResetEvery = resetEvery,
                IsRecurring = resetEvery != nameof(CrowdfundResetEvery.Never),
                UseAllStoreInvoices = app.TagAllInvoices,
                AppId = appId,
                SearchTerm = app.TagAllInvoices ? $"storeid:{app.StoreDataId}" : $"orderid:{AppService.GetAppOrderId(app)}",
                DisplayPerksRanking = settings.DisplayPerksRanking,
                DisplayPerksValue = settings.DisplayPerksValue,
                SortPerksByPopularity = settings.SortPerksByPopularity,
                Sounds = string.Join(Environment.NewLine, settings.Sounds),
                AnimationColors = string.Join(Environment.NewLine, settings.AnimationColors)
            };
            return View(vm);
        }

        [HttpPost("{appId}/settings/crowdfund")]
        public async Task<IActionResult> UpdateCrowdfund(string appId, UpdateCrowdfundViewModel vm, string command)
        {
            var app = GetCurrentApp();
            if (app == null)
                return NotFound();

            vm.TargetCurrency = await GetStoreDefaultCurrentIfEmpty(app.StoreDataId, vm.TargetCurrency);
            if (_currencies.GetCurrencyData(vm.TargetCurrency, false) == null)
                ModelState.AddModelError(nameof(vm.TargetCurrency), "Invalid currency");

            try
            {
                vm.PerksTemplate = _appService.SerializeTemplate(_appService.Parse(vm.PerksTemplate, vm.TargetCurrency));
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.PerksTemplate), "Invalid template");
            }
            if (vm.TargetAmount is decimal v && v == 0.0m)
            {
                vm.TargetAmount = null;
            }

            if (!vm.IsRecurring)
            {
                vm.ResetEvery = nameof(CrowdfundResetEvery.Never);
            }

            if (Enum.Parse<CrowdfundResetEvery>(vm.ResetEvery) != CrowdfundResetEvery.Never && !vm.StartDate.HasValue)
            {
                ModelState.AddModelError(nameof(vm.StartDate), "A start date is needed when the goal resets every X amount of time.");
            }

            if (Enum.Parse<CrowdfundResetEvery>(vm.ResetEvery) != CrowdfundResetEvery.Never && vm.ResetEveryAmount <= 0)
            {
                ModelState.AddModelError(nameof(vm.ResetEveryAmount), "You must reset the goal at a minimum of 1 ");
            }

            if (vm.DisplayPerksRanking)
            {
                vm.SortPerksByPopularity = true;
            }

            var parsedSounds = vm.Sounds?.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            ).Select(s => s.Trim()).ToArray();
            if (vm.SoundsEnabled && (parsedSounds == null || !parsedSounds.Any()))
            {
                vm.SoundsEnabled = false;
                parsedSounds = new CrowdfundSettings().Sounds;
            }

            var parsedAnimationColors = vm.AnimationColors?.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            ).Select(s => s.Trim()).ToArray();
            if (vm.AnimationsEnabled && (parsedAnimationColors == null || !parsedAnimationColors.Any()))
            {
                vm.AnimationsEnabled = false;
                parsedAnimationColors = new CrowdfundSettings().AnimationColors;
            }

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            app.Name = vm.AppName;
            var newSettings = new CrowdfundSettings
            {
                Title = vm.Title,
                Enabled = vm.Enabled,
                EnforceTargetAmount = vm.EnforceTargetAmount,
                StartDate = vm.StartDate?.ToUniversalTime(),
                TargetCurrency = vm.TargetCurrency,
                Description = vm.Description,
                EndDate = vm.EndDate?.ToUniversalTime(),
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
                DisplayPerksValue = vm.DisplayPerksValue,
                DisplayPerksRanking = vm.DisplayPerksRanking,
                SortPerksByPopularity = vm.SortPerksByPopularity,
                Sounds = parsedSounds,
                AnimationColors = parsedAnimationColors
            };

            app.TagAllInvoices = vm.UseAllStoreInvoices;
            app.SetSettings(newSettings);

            await _appService.UpdateOrCreateApp(app);

            _eventAggregator.Publish(new AppUpdated()
            {
                AppId = appId,
                StoreId = app.StoreDataId,
                Settings = newSettings
            });
            TempData[WellKnownTempData.SuccessMessage] = "App updated";
            return RedirectToAction(nameof(UpdateCrowdfund), new { appId });
        }
    }
}
