﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Events;
using BTCPayServer.Models;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Mails;
using BTCPayServer.Views.UIStoreMembership;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DisplayFormatter = BTCPayServer.Services.DisplayFormatter;

namespace BTCPayServer.Plugins.Subscriptions.Controllers;

[Authorize(Policy = Policies.CanViewMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
[Area(SubscriptionsPlugin.Area)]
public partial class UIOfferingController(
    ApplicationDbContextFactory dbContextFactory,
    IStringLocalizer stringLocalizer,
    LinkGenerator linkGenerator,
    EventAggregator eventAggregator,
    SubscriptionHostedService subsService,
    AppService appService,
    BTCPayServerEnvironment env,
    DisplayFormatter displayFormatter,
    EmailSenderFactory emailSenderFactory,
    IEnumerable<EmailTriggerViewModel> emailTriggers
) : UISubscriptionControllerBase(dbContextFactory, linkGenerator, stringLocalizer, subsService)
{
    [HttpPost("stores/{storeId}/offerings/{offeringId}/new-subscriber")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> NewSubscriber(
        string storeId, string offeringId,
        string planId,
        bool isTrial,
        int linkExpiration,
        string? prefilledEmail = null)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var plan = await ctx.Plans.GetPlanFromId(planId);
        if (plan is null)
            return NotFound();

        var checkoutData = new PlanCheckoutData()
        {
            PlanId = planId,
            IsTrial = plan.TrialDays > 0 && isTrial,
            NewSubscriber = true,
            TestAccount = env.CheatMode,
            SuccessRedirectUrl = LinkGenerator.OfferingLink(storeId, offeringId, SubscriptionSection.Subscribers, Request.GetRequestBaseUrl()),
            BaseUrl = Request.GetRequestBaseUrl(),
            Expiration = DateTimeOffset.UtcNow.AddDays(linkExpiration),
        };

        if (prefilledEmail != null && prefilledEmail.IsValidEmail())
            checkoutData.InvoiceMetadata = new InvoiceMetadata() { BuyerEmail = prefilledEmail }.ToJObject().ToString();
        ctx.PlanCheckouts.Add(checkoutData);
        await ctx.SaveChangesAsync();
        return RedirectToPlanCheckout(checkoutData.Id);
    }

    [HttpGet("stores/{storeId}/offerings")]
    public IActionResult CreateOffering(string storeId)
    {
        return View();
    }

    public class FormatCurrencyRequest
    {
        [JsonProperty("currency")]
        public string? Currency { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }
    }

    [HttpPost("stores/{storeId}/offerings/{offeringId}/format-currency")]
    [IgnoreAntiforgeryToken]
    public string FormatCurrency(string storeId, string offeringId, [FromBody] FormatCurrencyRequest? req)
        => displayFormatter.Currency(req?.Amount ?? 0m, req?.Currency ?? "USD", DisplayFormatter.CurrencyFormat.CodeAndSymbol);

    [HttpPost("stores/{storeId}/offerings/{offeringId}/Subscribers")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> SubscriberSuspend(string storeId, string offeringId, string customerId, string? command = null,
        string? suspensionReason = null, decimal? amount = null, string? description = null)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var sub = await ctx.Subscribers.GetByCustomerId(customerId, offeringId: offeringId, storeId: storeId);
        if (sub is null)
            return NotFound();
        var subName = sub.Customer.GetPrimaryIdentity() ?? sub.CustomerId;
        if (command is "unsuspend" or "suspend")
        {
            var canSwitch = (!sub.IsSuspended && command == "suspend") || (sub.IsSuspended && command == "unsuspend");
            if (canSwitch)
            {
                await SubsService.ToggleSuspend(sub.Id, suspensionReason);
                await ctx.Entry(sub).ReloadAsync();
                var message = sub.IsSuspended
                    ? StringLocalizer["Subscriber {0} is now suspended", subName]
                    : StringLocalizer["Subscriber {0} is now unsuspended", subName];
                TempData.SetStatusSuccess(message);
            }
        }
        else if (command is "toggle-test")
        {
            sub.TestAccount = !sub.TestAccount;
            await ctx.SaveChangesAsync();
            TempData.SetStatusSuccess(StringLocalizer["Subscriber {0} is now {1}", subName, sub.TestAccount ? "test" : "live"]);
        }
        else if (command is "credit" or "charge" && amount is > 0)
        {
            var message = command is "credit"
                ? StringLocalizer["Subscriber {0} has been credited", subName]
                : StringLocalizer["Subscriber {0} has been charged", subName];
            await SubsService.UpdateCredit(sub.Id, description ?? "Manual adjustment", command is "credit" ? amount.Value : -amount.Value);
            TempData.SetStatusSuccess(message);
        }

        return GoToOffering(storeId, offeringId, SubscriptionSection.Subscribers);
    }

    private RedirectToActionResult GoToOffering(string storeId, string offeringId, SubscriptionSection section = SubscriptionSection.Plans)
        => RedirectToAction(nameof(Offering), new { storeId, offeringId, section = section });

    [HttpPost("stores/{storeId}/offerings")]
    public async Task<IActionResult> CreateOffering(string storeId, CreateOfferingViewModel vm, string? command = null)
    {
        if (env.CheatMode && command == "create-fake")
        {
            return await CreateFakeOffering(storeId, vm);
        }

        if (!ModelState.IsValid)
            return View();

        var app = new AppData()
        {
            Name = vm.Name,
            AppType = SubscriptionsAppType.AppType,
            StoreDataId = storeId
        };
        app.SetSettings(new SubscriptionsAppType.AppConfig());
        await appService.UpdateOrCreateApp(app, sendEvents: false);

        await using var ctx = DbContextFactory.CreateContext();
        var o = new OfferingData()
        {
            AppId = app.Id,
        };
        ctx.Offerings.Add(o);
        await ctx.SaveChangesAsync();
        app.SetSettings(new SubscriptionsAppType.AppConfig()
        {
            OfferingId = o.Id
        });
        await appService.UpdateOrCreateApp(app, sendEvents: false);
        eventAggregator.Publish(new AppEvent.Created(app));
        this.TempData.SetStatusMessageModel(new()
        {
            Html = StringLocalizer["New offering created. You can now <a href='{0}' class='alert-link'>configure it.</a>",
                Url.Action(nameof(ConfigureOffering), new { storeId, offeringId = o.Id })!],
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return GoToOffering(storeId, o.Id, SubscriptionSection.Plans);
    }

    [HttpPost("stores/{storeId}/offerings/{offeringId}/Mails")]
    public async Task<IActionResult> SaveMailSettings(string storeId, string offeringId, SubscriptionsViewModel vm, string? addEmailRule = null)
    {
        await using var ctx = DbContextFactory.CreateContext();
        if (addEmailRule is not null)
        {
            var requestBase = Request.GetRequestBaseUrl();
            var link = LinkGenerator.CreateEmailRuleLink(storeId, requestBase, new()
            {
                OfferingId = offeringId,
                Trigger = addEmailRule,
                To = "{Subscriber.Email}",
                Condition =  $"$.Offering.Id == \"{offeringId}\"",
                RedirectUrl = new Uri(LinkGenerator.OfferingLink(storeId, offeringId, SubscriptionSection.Mails, requestBase)).AbsolutePath
            });
            return Redirect(link);
        }
        else
        {
            if (!ModelState.IsValid)
                return await Offering(storeId, offeringId, SubscriptionSection.Mails);
            var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
            if (offering is null)
                return NotFound();

            var update = offering.DefaultPaymentRemindersDays != vm.PaymentRemindersDays;
            if (update)
            {
                offering.DefaultPaymentRemindersDays = vm.PaymentRemindersDays;
                await ctx.SaveChangesAsync();
                this.TempData.SetStatusSuccess(StringLocalizer["Settings saved"]);
            }
            return GoToOffering(storeId, offeringId, SubscriptionSection.Mails);
        }
    }

    [HttpGet("stores/{storeId}/offerings/{offeringId}/{section}")]
    public async Task<IActionResult> Offering(string storeId, string offeringId, SubscriptionSection section = SubscriptionSection.Plans,
        string? checkoutPlanId = null, string? searchTerm = null)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
        if (offering is null)
            return NotFound();
        var plans = await ctx.Plans
            .Where(p => p.OfferingId == offeringId)
            .ToListAsync();

        if (checkoutPlanId is not null)
        {
            var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutPlanId);
            if (checkout is not null && checkout.Subscriber is { Customer: { } cust })
                TempData.SetStatusSuccess(StringLocalizer["Subscriber '{0}' successfully created", cust.GetPrimaryIdentity() ?? ""]);
        }

        var vm = new SubscriptionsViewModel(offering) { Section = section };
        vm.TotalPlans = plans.Count;
        vm.TotalSubscribers = plans.Select(p => p.MemberCount).Sum();
        var total = plans.Where(p => p.Currency == vm.Currency).Select(p => p.MonthlyRevenue).Sum();
        vm.TotalMonthlyRevenue = displayFormatter.Currency(total, vm.Currency, DisplayFormatter.CurrencyFormat.Symbol);

        vm.SelectablePlans = plans
            .Where(p => p.Status == PlanData.PlanStatus.Active)
            .OrderBy(p => p.Name)
            .Select((p, i) => new SubscriptionsViewModel.SelectablePlan(p.Name, p.Id, p.TrialDays > 0))
            .ToList();
        if (section == SubscriptionSection.Plans)
        {
            plans = plans
                .OrderBy(p => p.Status switch
                {
                    PlanData.PlanStatus.Active => 0,
                    _ => 1
                })
                .ThenByDescending(o => o.CreatedAt)
                .ToList();

            vm.Plans = plans.Select(p =>
                new SubscriptionsViewModel.PlanViewModel()
                {
                    Data = p
                }).ToList();
        }
        else if (section == SubscriptionSection.Subscribers)
        {
            // searchTerm
            int maxMembers = 100;
            var query = ctx.Subscribers
                .IncludeAll()
                .Where(m => m.OfferingId == offeringId);
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(u => (u.Customer.CustomerIdentities.Any(c => c.Value == searchTerm)) ||
                                         u.Customer.Name.Contains(searchTerm) ||
                                         (u.Customer.ExternalRef != null && u.Customer.ExternalRef.Contains(searchTerm)));
            }
            vm.SearchTerm = searchTerm;
            var members = query
                .OrderBy(m => m.IsActive ? 0 : 1)
                .ThenBy(m => m.Plan.Name)
                .ThenByDescending(m => m.CreatedAt)
                .Take(maxMembers)
                .ToList();
            vm.Subscribers = members
                .Select(v => new SubscriptionsViewModel.MemberViewModel()
                {
                    Data = v,
                }).ToList();
            vm.TooMuchSubscribers = members.Count == maxMembers;
        }
        else if (section == SubscriptionSection.Mails)
        {
            var settings = await emailSenderFactory.GetSettings(storeId);
            vm.EmailConfigured = settings is not null;
            vm.PaymentRemindersDays = offering.DefaultPaymentRemindersDays;
            vm.EmailRules = new();

            var triggers = emailTriggers
                .Where(t => WebhookSubscriptionEvent.IsSubscriptionTrigger(t.Trigger))
                .ToDictionary(t => t.Trigger);
            vm.AvailableTriggers = triggers.Values.ToList();
            foreach (var emailRule in
                     await ctx.EmailRules
                         .Where(r => r.StoreId == storeId && r.OfferingId == offeringId)
                         .ToListAsync())
            {
                if (!triggers.TryGetValue(emailRule.Trigger, out var triggerViewModel))
                    continue;
                vm.EmailRules.Add(new(emailRule)
                {
                    TriggerViewModel = triggerViewModel
                });
                triggers.Remove(triggerViewModel.Trigger);
            }
        }

        return View(nameof(Offering), vm);
    }

    [HttpGet("stores/{storeId}/offerings/{offeringId}/configure")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ConfigureOffering(string storeId, string offeringId)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
        if (offering is null)
            return NotFound();
        return View(new ConfigureOfferingViewModel(offering));
    }

    [HttpPost("stores/{storeId}/offerings/{offeringId}/configure")]
    public async Task<IActionResult> ConfigureOffering(
        string storeId,
        string offeringId,
        ConfigureOfferingViewModel vm,
        string? command = null,
        int? removeIndex = null)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
        if (offering is null)
            return NotFound();
        vm.Data = offering;
        bool itemsUpdated = false;
        if (command == "AddItem")
        {
            vm.Entitlements ??= new();
            vm.Entitlements.Add(new());
            itemsUpdated = true;
        }
        else if (removeIndex is int i)
        {
            vm.Entitlements.RemoveAt(i);
            itemsUpdated = true;
        }

        if (itemsUpdated)
        {
            this.ModelState.Clear();
            vm.Anchor = "entitlements";
        }

        if (!ModelState.IsValid || itemsUpdated)
            return View(vm);

        offering.SuccessRedirectUrl = vm.SuccessRedirectUrl;
        offering.App.Name = vm.Name;

        UpdateEntitlements(ctx, offering, vm);

        await ctx.SaveChangesAsync();
        this.TempData.SetStatusSuccess(StringLocalizer["Offering configuration updated"]);
        return GoToOffering(storeId, offeringId);
    }

    private static void UpdateEntitlements(ApplicationDbContext ctx, OfferingData offering, ConfigureOfferingViewModel vm)
    {
        var incomingById = vm.Entitlements
            .GroupBy(e => e.Id) // guard against dupes
            .ToDictionary(g => g.Key, g => g.First());

        var existingById = offering.Entitlements
            .ToDictionary(e => e.CustomId);


        var toRemove = offering.Entitlements
            .Where(e => !incomingById.ContainsKey(e.CustomId))
            .ToList();

        foreach (var e in toRemove)
            offering.Entitlements.Remove(e);

        ctx.Entitlements.RemoveRange(toRemove);

        foreach (var (id, vmEnt) in incomingById)
        {
            if (!existingById.TryGetValue(id, out var entity))
            {
                entity = new();
                entity.CustomId = vmEnt.Id;
                entity.OfferingId = offering.Id;
                offering.Entitlements.Add(entity);
            }

            entity.Description = vmEnt.ShortDescription;
        }
    }

    [HttpPost("stores/{storeId}/offerings/{offeringId}/plans/{planId}/delete-plan")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeletePlan(string storeId, string offeringId, string planId)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var plan = await ctx.Plans.GetPlanFromId(planId, offeringId, storeId);
        if (plan is null)
            return NotFound();
        var canDelete = !await ctx.Subscribers.Where(s => s.PlanId == planId).AnyAsync();
        if (!canDelete)
        {
            TempData.SetStatusMessageModel(new()
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Html = StringLocalizer["Cannot delete plan. It is currently in use by subscribers."]
            });
        }
        else
        {
            ctx.Plans.Remove(plan);
            await ctx.SaveChangesAsync();
            this.TempData.SetStatusSuccess(StringLocalizer["Plan deleted"]);
        }

        return GoToOffering(storeId, offeringId);
    }

    [HttpGet("stores/{storeId}/offerings/{offeringId}/add-plan")]
    [HttpGet("stores/{storeId}/offerings/{offeringId}/plans/{planId}/edit")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> AddPlan(string storeId, string offeringId, string? planId = null)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
        if (offering is null)
            return NotFound();

        var plan = planId is not null ? await ctx.Plans.GetPlanFromId(planId, offeringId, storeId) : null;
        if (plan is null && planId is not null)
            return NotFound();
        var vm = new AddEditPlanViewModel()
        {
            OfferingId = offeringId,
            PlanId = planId,
            OfferingName = offering.App.Name,
            Currency = this.HttpContext.GetStoreData().GetStoreBlob().DefaultCurrency,
            Price = plan?.Price ?? 0m,
            Name = plan?.Name ?? "",
            Description = plan?.Description ?? "",
            RecurringType = plan?.RecurringType ?? PlanData.RecurringInterval.Monthly,
            GracePeriodDays = plan?.GracePeriodDays ?? 0,
            TrialDays = plan?.TrialDays ?? 0,
            OptimisticActivation = plan?.OptimisticActivation ?? false,
            Renewable = plan?.Renewable ?? true,
            PlanChanges = offering.Plans
                .Where(p => p.Id != planId && p.Status == PlanData.PlanStatus.Active)
                .Select(p => new AddEditPlanViewModel.PlanChange()
                {
                    PlanId = p.Id,
                    PlanName = p.Name,
                    SelectedType = plan?.PlanChanges
                        .FirstOrDefault(pc => pc.PlanChangeId == p.Id)?
                        .Type.ToString() ?? "None"
                })
                .OrderBy(p => p.PlanName)
                .ToList(),
            Entitlements = offering.Entitlements.OrderBy(e => e.CustomId).Select(e => new AddEditPlanViewModel.Entitlement()
            {
                CustomId = e.CustomId,
                ShortDescription = e.Description,
                Selected = plan?.GetEntitlement(e.Id) is not null
            }).ToList(),
        };

        return View(vm);
    }

    [HttpPost("stores/{storeId}/offerings/{offeringId}/add-plan")]
    [HttpPost("stores/{storeId}/offerings/{offeringId}/plans/{planId}/edit")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> AddPlan(string storeId, string offeringId, AddEditPlanViewModel vm, string? planId = null, string? command = null,
        int? removeIndex = null)
    {
        if (!ModelState.IsValid)
            return await AddPlan(storeId, offeringId, planId);
        await using var ctx = DbContextFactory.CreateContext();
        var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
        // Check if the offering is part of the store
        if (offering is null)
            return NotFound();

        var plan = planId is not null ? await ctx.Plans.GetPlanFromId(planId, offeringId, storeId) : null;
        if (plan is null && planId is not null)
            return NotFound();

        plan ??= new PlanData();
        plan.Name = vm.Name;
        plan.Description = vm.Description;
        plan.Price = vm.Price;
        plan.Currency = vm.Currency;
        plan.GracePeriodDays = vm.GracePeriodDays;
        plan.TrialDays = vm.TrialDays;
        plan.OptimisticActivation = vm.OptimisticActivation;
        plan.Renewable = vm.Renewable;
        if (planId is null)
            plan.CreatedAt = DateTimeOffset.UtcNow;
        plan.RecurringType = vm.RecurringType;
        plan.OfferingId = vm.OfferingId;
        plan.PlanEntitlements ??= new();
        plan.PlanChanges ??= new();

        foreach (var vmPC in vm.PlanChanges)
        {
            var existing = plan.PlanChanges.FirstOrDefault(pc => pc.PlanChangeId == vmPC.PlanId);
            if (vmPC.SelectedType == "None" && existing is not null)
            {
                plan.PlanChanges.Remove(existing);
                ctx.PlanChanges.Remove(existing);
            }

            if (vmPC.SelectedType == "None" && existing is null)
            {
                continue;
            }
            else if (existing is null)
            {
                var pc = new PlanChangeData();
                pc.PlanId = plan.Id;
                pc.PlanChangeId = vmPC.PlanId;
                plan.PlanChanges.Add(pc);
                ctx.PlanChanges.Add(pc);
                existing = pc;
            }

            existing.Type = vmPC.SelectedType switch
            {
                "Upgrade" => PlanChangeData.ChangeType.Upgrade,
                "Downgrade" => PlanChangeData.ChangeType.Downgrade,
                _ => PlanChangeData.ChangeType.Downgrade
            };
        }

        if (planId is null)
        {
            ctx.Plans.Add(plan);
        }

        await ctx.SaveChangesAsync();
        eventAggregator.Publish(new SubscriptionEvent.PlanUpdated(plan));

        var customIdsToIds = offering.Entitlements.ToDictionary(x => x.CustomId, x => x.Id);
        var enabled = vm.Entitlements.Where(e => e.Selected).Select(e => customIdsToIds[e.CustomId]).ToArray();
        await ctx.Database.GetDbConnection()
            .ExecuteAsync("""
                          DELETE FROM subs_plans_entitlements
                          WHERE plan_id = @planId AND NOT (entitlement_id = ANY(@enabled));
                          INSERT INTO subs_plans_entitlements(plan_id, entitlement_id)
                          SELECT @planId, e FROM unnest(@enabled) e
                          ON CONFLICT DO NOTHING;
                          """, new { planId = plan.Id, enabled });

        if (planId is null)
            this.TempData.SetStatusSuccess(StringLocalizer["New plan created"]);
        else
            this.TempData.SetStatusSuccess(StringLocalizer["Plan edited"]);
        return GoToOffering(plan.Offering.App.StoreDataId, plan.OfferingId);
    }

    [HttpGet("stores/{storeId}/offerings/{offeringId}/subscribers/{customerId}/create-portal")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CreatePortalSession(string storeId, string offeringId, string customerId)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var sub = await ctx.Subscribers.GetByCustomerId(customerId, offeringId: offeringId, storeId: storeId);
        if (sub is null)
            return NotFound();
        var portal = new PortalSessionData()
        {
            SubscriberId = sub.Id,
            Expiration = DateTimeOffset.UtcNow + TimeSpan.FromHours(1.0),
            BaseUrl = Request.GetRequestBaseUrl()
        };
        ctx.PortalSessions.Add(portal);
        await ctx.SaveChangesAsync();
        return RedirectToSubscriberPortal(portal.Id);
    }
}
