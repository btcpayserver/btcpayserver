#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.Monetization.Views;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Views.UIStoreMembership;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;

namespace BTCPayServer.Plugins.Monetization.Controllers;

[Authorize(Policy = Client.Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("server/monetization")]
[Area(MonetizationPlugin.Area)]
public class UIServerMonetizationController
    (ApplicationDbContextFactory contextFactory,
        LinkGenerator linkGenerator,
        SettingsRepository settingsRepository,
        AppService appService,
        IStringLocalizer stringLocalizer) : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    [HttpGet]
    public async Task<IActionResult> Monetization()
    {
        await using var ctx = contextFactory.CreateContext();
        var settings = await settingsRepository.GetSettingAsync<MonetizationSettings>();
        var vm = new MonetizationViewModel()
        {
            Settings = settings ?? new MonetizationSettings()
        };
        if (settings is not null)
        {
            vm.DefaultPlan = await ctx.Plans.GetPlanFromId(settings.DefaultPlanId ?? "");
            vm.Offering = await ctx.Offerings.GetOfferingData(settings.OfferingId ?? "");
        }

        return View(vm);
    }
    [HttpPost]
    public async Task<IActionResult> Monetization(string command)
    {
        var selectedStore = this.HttpContext.GetCurrentStoreId();
        if (selectedStore is null)
        {
            TempData.SetStatusMessageModel(new()
            {
                Message = "You need to select a store first",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(Monetization));
        }

        await using var ctx = contextFactory.CreateContext();
        var store = await ctx.Stores.FindAsync(selectedStore);
        var currency = store!.GetStoreBlob().DefaultCurrency;
        if (command == "activate-monetization")
        {
            var (_, offeringId) = await appService.CreateOffering(selectedStore, "BTCPay Server Access");

            var entitlements = new[]
            {
                ("can-login", "Can login to BTCPay Server"),
                ("one-store", "Can create a one store"),
                ("unlimited-store", "Can create more than one store"),
            }.Select(e => new EntitlementData()
            {
                CustomId = e.Item1,
                Description = e.Item2,
                OfferingId = offeringId,
            }).ToDictionary(e => e.CustomId, e => e);
            foreach (var e in entitlements.Values)
            {
                ctx.Entitlements.Add(e);
            }

            PlanData freePlan = new()
            {
                Name = "Free Plan",
                RecurringType = PlanData.RecurringInterval.Monthly,
                Renewable = false,
                Currency = currency,
                OfferingId = offeringId,
            };
            ctx.Plans.Add(freePlan);
            ctx.PlanEntitlements.AddRange(
                new[] { "can-login", "one-store" }
                .Select(e => new PlanEntitlementData()
                {
                    Plan = freePlan,
                    Entitlement = entitlements[e],
                }));

            PlanData paidPlan = new()
            {
                Name = "Paid Plan",
                RecurringType = PlanData.RecurringInterval.Monthly,
                Price = 10m,
                Currency = currency,
                Renewable = true,
                OfferingId = offeringId
            };
            ctx.Plans.Add(paidPlan);
            ctx.PlanEntitlements.AddRange(
                new[] { "can-login", "one-store", "unlimited-store" }
                    .Select(e => new PlanEntitlementData()
                    {
                        Plan = paidPlan,
                        Entitlement = entitlements[e],
                    }));

            ctx.PlanChanges.Add(new()
            {
                Plan = freePlan,
                PlanChange = paidPlan,
                Type = PlanChangeData.ChangeType.Upgrade
            });
            await ctx.SaveChangesAsync();
            var settings = await settingsRepository.GetSettingAsync<MonetizationSettings>() ?? new();
            settings.OfferingId = offeringId;
            settings.DefaultPlanId = freePlan.Id;
            await settingsRepository.UpdateSetting(settings);
            TempData.SetStatusMessageModel(new()
            {
                Message = StringLocalizer["Monetization activated, users who register to your server from now will be subscriber of this offering."],
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(Monetization));
        }
        return RedirectToAction(nameof(Monetization));
    }

    private RedirectResult RedirectToOffering(string selectedStore, string offeringId)
    {
        return Redirect(linkGenerator.OfferingLink(selectedStore, offeringId, SubscriptionSection.Plans, Request.GetRequestBaseUrl()));
    }
}
