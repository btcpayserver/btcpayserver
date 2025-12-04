#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.Monetization.Views;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Plugins.Subscriptions.Controllers;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Views.UIStoreMembership;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
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
    ApplicationDbContext ctx,
    MonetizationHostedService monetizationService,
    SettingsRepository settingsRepository,
    AppService appService,
    CurrencyNameTable currencyNameTable,
    UserManager<ApplicationUser> userManager,
    StoreRepository storeRepo,
    ViewLocalizer viewLocalizer,
    EmailSenderFactory emailSenderFactory,
    LinkGenerator linkGenerator,
    IStringLocalizer stringLocalizer) : Controller
{
    public ViewLocalizer ViewLocalizer { get; } = viewLocalizer;
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    [HttpGet]
    public async Task<IActionResult> Monetization()
    {
        var settings = await settingsRepository.GetSettingAsync<MonetizationSettings>() ?? new();
        var vm = new MonetizationViewModel()
        {
            Settings = settings
        };

        var offeringAndPlan = await ctx.GetOfferingAndPlan(settings);
        vm.DefaultPlan = offeringAndPlan?.Plan;
        vm.Offering = offeringAndPlan?.Offering;

        var activePlans = vm.Offering?.Plans.Where(p => p.Status == PlanData.PlanStatus.Active).ToArray() ?? [];
        vm.EmailServerConfigured = (await (await emailSenderFactory.GetEmailSender()).GetEmailSettings())?.IsComplete() is true;
        vm.EmailStoreConfigured = vm.Offering is not null &&
                                  (await (await emailSenderFactory.GetEmailSender(vm.Offering.App.StoreDataId)).GetEmailSettings())?.IsComplete() is true;

        vm.Step =
            offeringAndPlan is null ? MonetizationViewModel.InstallStatus.SetOffering :
            !vm.EmailServerConfigured ? MonetizationViewModel.InstallStatus.ConfigureServerEmail :
            !vm.EmailStoreConfigured ? MonetizationViewModel.InstallStatus.ConfigureStoreEmail :
            MonetizationViewModel.InstallStatus.Done;

        HashSet<string> canLogin = new();
        if (vm.Offering is not null)
        {
            var planIds = activePlans.Select(p => p.Id).Distinct().ToArray();
            canLogin = (await ctx.PlanFeatures.Where(p => planIds.Contains(p.PlanId))
                .Where(o => o.Feature.CustomId == MonetizationFeatures.CanAccess)
                .Select(o => o.PlanId)
                .ToArrayAsync()).ToHashSet();
        }

        var stores = await storeRepo.GetStoresByUserId(userManager.GetUserId(User)!);
        if (vm.Offering is null)
        {
            var vmSelect = new SelectExistingOfferingModalViewModel();
            var storeIds = stores.Select(s => s.Id).ToArray();
            var offerings = await ctx
                .Offerings
                .Include(o => o.App)
                .Include(o => o.Plans)
                .Where(o => storeIds.Contains(o.App.StoreDataId))
                .ToArrayAsync();
            var offeringsByStores = offerings.GroupBy(o => o.App.StoreDataId).ToDictionary(o => o.Key, o => o.ToArray());
            vmSelect.Stores =
                stores
                    .Where(s => offeringsByStores.ContainsKey(s.Id))
                    .Select(s => new SelectExistingOfferingModalViewModel.Store()
                    {
                        Id = s.Id,
                        Name = s.StoreName,
                        Offerings = offeringsByStores[s.Id]
                            .Where(o => !o.App.Archived)
                            .Select(o => new SelectExistingOfferingModalViewModel.Offering()
                            {
                                Id = o.Id,
                                Name = o.App.Name,
                                Plans = o.Plans
                                    .Where(p => p.Status == PlanData.PlanStatus.Active)
                                    .Select(p => new SelectExistingOfferingModalViewModel.Item()
                                    {
                                        Id = p.Id,
                                        Name = p.Name
                                    })
                                    .OrderBy(p => p.Name)
                                    .ToList()
                            })
                            .OrderBy(p => p.Name)
                            .ToList()
                    })
                    .OrderBy(p => p.Name)
                    .ToList();

            // Remove empty
            foreach (var store in vmSelect.Stores)
            {
                store.Offerings = store.Offerings.Where(o => o.Plans.Count != 0).ToList();
            }

            vmSelect.Stores = vmSelect.Stores.Where(s => s.Offerings.Count != 0).ToList();
            if (vmSelect.Stores.Count > 0)
                vm.SelectExistingOfferingModal = vmSelect;
        }

        string GetLabel(PlanData p) => canLogin.Contains(p.Id) ? $"{p.Name} (can-access)" : p.Name;
        var storeId = HttpContext.GetCurrentStoreId();
        vm.ActivateModal = new ActivateMonetizationModelViewModel(storeId, stores);
        vm.MigrateUsersModal = new MigrateUsersModalViewModel()
        {
            AvailablePlans = activePlans.OrderBy(p => p.Name).Select(p => new SelectListItem(GetLabel(p), p.Id)).ToList(),
            SelectedPlanId = vm.DefaultPlan?.Id ?? ""
        };
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Monetization(MonetizationViewModel vm, string command)
    {
        if (command == "activate-monetization" && vm.ActivateModal is {} activateModal)
        {
            var selectedStore = vm.ActivateModal?.SelectedStoreId ?? "";
            var store = await storeRepo.FindStore(selectedStore, userManager.GetUserId(User) ?? "");
            if (store is null)
            {
                TempData.SetStatusMessageModel(new()
                {
                    Message = "You need to select a store first",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(Monetization));
            }

            if (!ModelState.IsValid)
                return await Monetization();
            var (_, offeringId) = await appService.CreateOffering(selectedStore, "BTCPay Server Access");

            var features = CreateDefaultFeatures(offeringId);
            foreach (var e in features.Values)
            {
                ctx.Features.Add(e);
            }

            var currency = store.GetStoreBlob().DefaultCurrency;
            var price = activateModal.StarterPlanCost;
            price = Math.Round(price, currencyNameTable.GetNumberFormatInfo(currency)?.CurrencyDecimalDigits ?? 2);
            PlanData starterPlan = new()
            {
                Name = "Starter Plan",
                RecurringType = PlanData.RecurringInterval.Monthly,
                TrialDays = activateModal.TrialDays,
                Currency = currency,
                Price = price,
                OfferingId = offeringId,
                OptimisticActivation = false
            };
            ctx.Plans.Add(starterPlan);
            ctx.PlanFeatures.AddRange(
                new[] { MonetizationFeatures.CanAccess }
                    .Select(e => new PlanFeatureData()
                    {
                        Plan = starterPlan,
                        Feature = features[e],
                    }));
            var serverBase = Request.GetRequestBaseUrl().ToString();
            if (store.StoreWebsite != serverBase)
                store.StoreWebsite = serverBase;
            await ctx.SaveChangesAsync();

            await UpdatePoliciesSettings(true);

            var serverSettings = await settingsRepository.GetSettingAsync<ServerSettings>() ?? new();
            if (serverSettings.BaseUrl != serverBase)
            {
                serverSettings.BaseUrl = serverBase;
                await settingsRepository.UpdateSetting(serverSettings);
            }

            var defaultPlanId = starterPlan.Id;
            List<EmailRuleData> emailRules =
            [
                new()
                {
                    Trigger = "WH-" + WebhookSubscriptionEvent.PaymentReminder,
                    Subject = "Payment reminder for your subscription",
                    Body = EmailsPlugin.CreateEmail(
                        "In order to renew your subscription, please renew before expiration.",
                        "Go to Subscription portal", linkGenerator.UserManageBillingLink(Request.GetRequestBaseUrl())),
                    Condition = UIOfferingController.CreateOfferingCondition(offeringId)
                },
                new()
                {
                    Trigger = "WH-" + WebhookSubscriptionEvent.SubscriberPhaseChanged,
                    Subject = "Your subscription has expired",
                    Body = EmailsPlugin.CreateEmail(
                        "Your access has expired. Please renew your subscription to continue using it.",
                        "Go to Subscription portal", linkGenerator.UserManageBillingLink(Request.GetRequestBaseUrl())),
                    Condition = UIOfferingController.CreateOfferingCondition(offeringId, SubscriberData.PhaseTypes.Expired)
                }
            ];

            foreach (var rule in emailRules)
            {
                rule.StoreId = store.Id;
                rule.OfferingId = offeringId;
                rule.To = ["{Subscriber.Email}"];
                ctx.EmailRules.Add(rule);
            }

            await ctx.SaveChangesAsync();

            var migratedUsers = 0;
            if (activateModal.MigrateExistingUsers)
            {
                migratedUsers = (await monetizationService.MigrateUsers(offeringId, starterPlan.Id)).Length;
            }

            if (await ctx.Offerings.GetOfferingData(offeringId) is { } off)
            {
                var settings = await settingsRepository.GetSettingAsync<MonetizationSettings>() ?? new();
                settings.OfferingId = offeringId;
                settings.DefaultPlanId = defaultPlanId;
                await settingsRepository.UpdateSetting(settings);

                var migratedUserText = migratedUsers > 0 ? StringLocalizer["({0} migrated users)", migratedUsers].Value : "";
                var offeringUrl = linkGenerator.OfferingLink(off.App.StoreDataId, off.Id, SubscriptionSection.Plans, Request.GetRequestBaseUrl());
                TempData.SetStatusMessageModel(new()
                {
                    LocalizedHtml = ViewLocalizer[
                        "Monetization activated, users who register to your server from now will be subscriber of <a class=\"alert-link\" href=\"{0}\">this offering</a>.{1}",
                        offeringUrl, migratedUserText],
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }
        }
        else if (command == "change-offering")
        {
            var settings = new MonetizationSettings()
            {
                OfferingId = vm.SelectExistingOfferingModal?.SelectedOfferingId,
                DefaultPlanId = vm.SelectExistingOfferingModal?.SelectedPlanId
            };
            if (await ctx.GetOfferingAndPlan(settings) is { } v)
            {
                await settingsRepository.UpdateSetting(settings);
                await UpdatePoliciesSettings(true);
                TempData.SetStatusMessageModel(new()
                {
                    Message = StringLocalizer["Monetization order updated to offering {0} with default plan {1}.", v.Offering.App.Name, v.Plan.Name],
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }
        }
        else if (command == "migrate-users")
        {
            var settings = await settingsRepository.GetSettingAsync<MonetizationSettings>();
            if (await ctx.GetOfferingAndPlan(settings) is { } v)
            {
                var count = (await monetizationService.MigrateUsers(v.Offering.Id, vm.MigrateUsersModal?.SelectedPlanId)).Length;
                // Should we fire NewSubscriber event?
                // Given this is a one time operation maybe not...
                // This means the email rules won't be triggered
                // Anyway, if we do, we should do it on a separate task to not block this method.
                TempData.SetStatusMessageModel(new()
                {
                    Message = StringLocalizer["{0} users migrated to the plan '{1}'.", count, v.Plan.Name],
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }
        }
        else if (command == "demonetize")
        {
            var settings = await settingsRepository.GetSettingAsync<MonetizationSettings>() ?? new();
            settings.DefaultPlanId = null;
            settings.OfferingId = null;
            await settingsRepository.UpdateSetting(settings);
            await UpdatePoliciesSettings(false);
            TempData.SetStatusMessageModel(new()
            {
                Message = StringLocalizer["Monetization deactivated, users who register to your server from now will not be subscriber of any offering."],
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else if (command == "copy-server-email-settings")
        {
            var settings = await settingsRepository.GetSettingAsync<MonetizationSettings>();
            var offeringAndPlan = await ctx.GetOfferingAndPlan(settings);
            if (offeringAndPlan is { Offering: { } offering } &&
                await storeRepo.FindStore(offering.App.StoreDataId, userManager.GetUserId(User) ?? "") is { } store)
            {
                var storeBlob = store.GetStoreBlob();
                var policies = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new();
                if (policies.DisableStoresToUseServerEmailSettings)
                {
                    var serverSettings = await settingsRepository.GetSettingAsync<EmailSettings>() ?? new();
                    storeBlob.EmailSettings = serverSettings;
                    TempData.SetStatusMessageModel(new()
                    {
                        Message = StringLocalizer["Store emails settings copied from server settings"],
                        Severity = StatusMessageModel.StatusSeverity.Success
                    });
                }
                else
                {
                    storeBlob.EmailSettings = null;
                    TempData.SetStatusMessageModel(new()
                    {
                        Message = StringLocalizer["Store emails settings are now using the server's SMTP settings."],
                        Severity = StatusMessageModel.StatusSeverity.Success
                    });
                }

                store.SetStoreBlob(storeBlob);
                await storeRepo.UpdateStoreBlob(store);
            }
        }

        return RedirectToAction(nameof(Monetization));
    }

    private async Task UpdatePoliciesSettings(bool monetization)
    {
        var registrationLink = Url.Action(action: nameof(UIUserMonetizationController.NewUser), controller: "UIUserMonetization");
        var policies = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new();
        policies.RequiresConfirmedEmail = true;
        if (monetization && policies.RegisterPageRedirect is null)
            policies.RegisterPageRedirect = registrationLink;
        if (!monetization && policies.RegisterPageRedirect == registrationLink)
            policies.RegisterPageRedirect = null;
        await settingsRepository.UpdateSetting(policies);
    }

    private Dictionary<string, FeatureData> CreateDefaultFeatures(string offeringId)
    {
        var features = new[]
        {
            (MonetizationFeatures.CanAccess, StringLocalizer["Can access BTCPay Server"].Value),
        }.Select(e => new FeatureData()
        {
            CustomId = e.Item1,
            Description = e.Item2,
            OfferingId = offeringId,
        }).ToDictionary(e => e.CustomId, e => e);
        return features;
    }
}
