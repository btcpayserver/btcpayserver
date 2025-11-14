#nullable enable
using System;
using System.Collections.Generic;
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
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;

namespace BTCPayServer.Plugins.Monetization.Controllers;

[Authorize(Policy = Client.Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("server/monetization")]
[Area(MonetizationPlugin.Area)]
public class UIServerMonetizationController(
    ApplicationDbContextFactory contextFactory,
    MonetizationHostedService monetizationService,
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
        }

        var storeId = HttpContext.GetCurrentStoreId();
        var offerings = await ctx.Offerings
            .Include(o => o.App)
            .Include(o => o.Plans)
            .Where(o => o.App.StoreDataId == storeId && !o.App.Archived)
            .OrderBy(o => o.App.Name)
            .ToListAsync();

        var offering = offerings?.FirstOrDefault(o => o.Id == settings?.OfferingId);
        var activePlans = offering?.Plans.Where(p => p.Status == PlanData.PlanStatus.Active).ToArray() ?? [];
        var defaultPlan = activePlans.FirstOrDefault(p => p.Id == settings?.DefaultPlanId);
        HashSet<string> canLogin = new();
        if (offering is not null)
        {
            var planIds = activePlans.Select(p => p.Id).Distinct().ToArray();
            canLogin = (await ctx.PlanEntitlements.Where(p => planIds.Contains(p.PlanId))
                .Where(o => o.Entitlement.CustomId == MonetizationEntitlments.CanLogin)
                .Select(o => o.PlanId)
                .ToArrayAsync()).ToHashSet();
        }

        string GetLabel(PlanData p) => canLogin.Contains(p.Id) ? StringLocalizer["{0} (Can login)", p.Name] : p.Name;
        vm.ActivateModal = new ActivateMonetizationModelViewModel(settings, offerings);
        vm.MigrateUsersModal = new MigrateUsersModalViewModel()
        {
            AvailablePlans = activePlans.OrderBy(p => p.Name).Select(p => new SelectListItem(GetLabel(p), p.Id)).ToList() ?? [],
            SelectedPlanId = defaultPlan?.Id ?? ""
        };
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Monetization(MonetizationViewModel vm, string command)
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
            string? offeringId;
            string? defaultPlanId;
            if (vm is { ActivateModal: { SelectedOfferingId: { } selectedOfferingId, SelectedPlanId: { } defaultPlanId2 } })
            {
                var offering = await ctx.Offerings.GetOfferingData(selectedOfferingId, selectedStore);
                offeringId = offering?.Id;
                var plan = offering?.Plans.FirstOrDefault(p => p.Id == defaultPlanId2);
                defaultPlanId = plan?.Id;
                if (offeringId is not null && offering is not null)
                {
                    var defaultEntitlement = CreateDefaultEntitlements(offeringId).Values;
                    var existingCustomIds = offering.Entitlements.Select(e => e.CustomId).ToHashSet(StringComparer.Ordinal);
                    var toAdd = defaultEntitlement.Where(e => !existingCustomIds.Contains(e.CustomId)).ToList();
                    if (toAdd.Count != 0)
                    {
                        ctx.Entitlements.AddRange(toAdd);
                        await ctx.SaveChangesAsync();
                    }
                }
            }
            else
            {
                (_, offeringId) = await appService.CreateOffering(selectedStore, "BTCPay Server Access");

                var entitlements = CreateDefaultEntitlements(offeringId);
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
                    new[] { MonetizationEntitlments.CanLogin, "one-store" }
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
                    new[] { MonetizationEntitlments.CanLogin, "one-store", "unlimited-store" }
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
                defaultPlanId = freePlan.Id;
            }
            var settings = await settingsRepository.GetSettingAsync<MonetizationSettings>() ?? new();
            settings.OfferingId = offeringId;
            settings.DefaultPlanId = defaultPlanId;
            await settingsRepository.UpdateSetting(settings);
            TempData.SetStatusMessageModel(new()
            {
                Message = StringLocalizer["Monetization activated, users who register to your server from now will be subscriber of this offering."],
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else if (command == "migrate-users")
        {
            var settings = await settingsRepository.GetSettingAsync<MonetizationSettings>() ?? new();
            var plan = await ctx.Plans.GetPlanFromId(vm.MigrateUsersModal?.SelectedPlanId ?? "");
            var count = await monetizationService.MigrateUsers(settings.OfferingId, vm.MigrateUsersModal?.SelectedPlanId);
            TempData.SetStatusMessageModel(new()
            {
                Message = StringLocalizer["{0} users migrated to the plan '{1}'.", count, plan?.Name ?? ""],
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else if (command == "demonetize")
        {
            var settings = await settingsRepository.GetSettingAsync<MonetizationSettings>() ?? new();
            settings.DefaultPlanId = null;
            settings.OfferingId = null;
            await settingsRepository.UpdateSetting(settings);
            TempData.SetStatusMessageModel(new()
            {
                Message = StringLocalizer["Monetization deactivated, users who register to your server from now will not be subscriber of any offering."],
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }

        return RedirectToAction(nameof(Monetization));
    }

    private static Dictionary<string, EntitlementData> CreateDefaultEntitlements(string offeringId)
    {
        var entitlements = new[]
        {
            (MonetizationEntitlments.CanLogin, "Can login to BTCPay Server"),
            ("one-store", "Can create a one store"),
            ("unlimited-store", "Can create more than one store"),
        }.Select(e => new EntitlementData()
        {
            CustomId = e.Item1,
            Description = e.Item2,
            OfferingId = offeringId,
        }).ToDictionary(e => e.CustomId, e => e);
        return entitlements;
    }

    private RedirectResult RedirectToOffering(string selectedStore, string offeringId)
    {
        return Redirect(linkGenerator.OfferingLink(selectedStore, offeringId, SubscriptionSection.Plans, Request.GetRequestBaseUrl()));
    }
}
