#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Views.UIStoreMembership;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIStoreSubscriptionsController(
    ApplicationDbContextFactory dbContextFactory,
    IStringLocalizer StringLocalizer,
    UriResolver uriResolver,
    UIInvoiceController invoiceController,
    CallbackGenerator callbackGenerator,
    EventAggregator eventAggregator,
    SubscriptionHostedService subsService,
    AppService appService
    ) : Controller
{
    [HttpGet("plan-checkout/{planId}")]
    [AllowAnonymous]
    public async Task<IActionResult> PlanCheckout(string planId, string? prefilledEmail = null)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var plan = await GetPlanFromId(planId, ctx);
        if (plan is null)
            return NotFound();

        var vm = new PlanCheckoutViewModel()
        {
            Id = planId,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, plan.Offering.App.StoreData.GetStoreBlob()),
            StoreName = plan.Offering.App.StoreData.StoreName,
            Title = plan.Name,
            Data = plan,
            Email = prefilledEmail,
            IsPrefilled = prefilledEmail?.IsValidEmail() is true,
            IsTrial = plan.TrialDays > 0
        };
        return View(vm);
    }

    private static async Task<PlanData?> GetPlanFromId(string planId, ApplicationDbContext ctx)
    {
        var plan = await ctx.Plans
            .Include(o => o.Offering).ThenInclude(o => o.App).ThenInclude(o => o.StoreData)
            .Include(o => o.PlanEntitlements).ThenInclude(o => o.Entitlement)
            .Where(p => p.Id == planId)
            .FirstOrDefaultAsync();
        return plan;
    }

    [HttpPost("plan-checkout/{planId}")]
    [AllowAnonymous]
    public async Task<IActionResult> PlanCheckout(string planId, PlanCheckoutViewModel vm, string? prefilledEmail = null, string? command = null, CancellationToken cancellationToken = default)
    {
        if (!vm.Email.IsValidEmail())
            ModelState.AddModelError(nameof(vm.Email), "Invalid email format");
        if (!ModelState.IsValid)
            return await PlanCheckout(planId, prefilledEmail);
        await using var ctx = dbContextFactory.CreateContext();
        var plan = await GetPlanFromId(planId, ctx);
        if (plan is null)
            return NotFound();
        if (command is "pay")
        {
            var metadata = new InvoiceMetadata()
            {
                BuyerEmail = vm.Email,
            }.ToJObject();
            metadata.Add("planId", plan.Id);
            var request = await invoiceController.CreateInvoiceCoreRaw(new()
                {
                    Currency = plan.Currency,
                    Amount = plan.Price,
                    Metadata = metadata
                }, plan.Offering.App.StoreData, Request.GetAbsoluteRoot(),
                [GetPlanInvoiceTag(plan.Id)]);

            var link = callbackGenerator.InvoiceCheckoutLink(request.Id, Request);
            return Redirect(link);
        }
        else // if (command is "start-trial")
        {
            await subsService.ExecuteStartTrial(new(plan.Offering.App.StoreDataId, plan.Id, vm.Email), cancellationToken);
            return NotFound();
        }
    }

    public static string GetPlanInvoiceTag(string planId) => $"SUBS#{planId}";
    public static string? GetPlanIdFromInvoice(InvoiceEntity invoiceEntiy) => invoiceEntiy.GetInternalTags("SUBS#").FirstOrDefault();

    [HttpGet("stores/{storeId}/offerings")]
    public IActionResult CreateOffering(string storeId)
    {
        return View();
    }

    [HttpPost("stores/{storeId}/offerings")]
    public async Task<IActionResult> CreateOffering(string storeId, CreateOfferingViewModel vm)
    {
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

        await using var ctx = dbContextFactory.CreateContext();
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
            Html = StringLocalizer["New offering created. You can now <a href='{0}' class='alert-link'>configure it.</a>", Url.Action(nameof(ConfigureOffering), new { storeId, offeringId = o.Id })!],
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(Offering), new{ storeId, offeringId = o.Id });
    }

    [HttpGet("stores/{storeId}/offerings/{offeringId}/{section=Plans}")]
    public async Task<IActionResult> Offering(string storeId, string offeringId, SubscriptionSection section = SubscriptionSection.Plans)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var offering = await GetOfferingData(ctx, storeId, offeringId);
        if (offering is null)
            return NotFound();
        var plans = await ctx.Plans
            .Where(p => p.OfferingId == offeringId)
            .ToListAsync();

        var vm = new SubscriptionsViewModel(offering) { Section = section };
        vm.TotalPlans = plans.Count;
        // TODO: This is wasteful, we should pre-calculate this, if too much subscribers this page will start hanging...
        vm.TotalSubscribers = await ctx.Subscribers.Where(s => s.OfferingId == offeringId && s.IsActive).CountAsync();
        if (section == SubscriptionSection.Plans)
        {
            plans = plans
                .OrderBy(p => p.Status switch
                {
                    PlanData.PlanStatus.Active => 0,
                    PlanData.PlanStatus.Draft => 1,
                    _ => 2,
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
            var members = ctx.Subscribers
                .Include(m => m.Plan)
                .Include(m => m.Customer)
                .Where(m => m.OfferingId == offeringId)
                .ToList();
            vm.Subscribers = members
                .Select(v => new SubscriptionsViewModel.MemberViewModel()
                {
                    Data = v
                }).ToList();
        }

        return View(vm);
    }

    [HttpGet("stores/{storeId}/offerings/{offeringId}/configure")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ConfigureOffering(string storeId, string offeringId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var offering = await GetOfferingData(ctx, storeId, offeringId);
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

        await using var ctx = dbContextFactory.CreateContext();
        var offering = await GetOfferingData(ctx, storeId, offeringId);
        if (offering is null)
            return NotFound();

        foreach (var entitlement in vm.Entitlements)
        {
            if (!string.IsNullOrWhiteSpace(entitlement.Name) && string.IsNullOrWhiteSpace(entitlement.Id))
            {
                entitlement.Id = GenerateEntitlementId(entitlement);
            }
        }
        UpdateEntitlements(ctx, offering, vm);

        await ctx.SaveChangesAsync();
        this.TempData.SetStatusSuccess(StringLocalizer["Offering configuration updated"]);
        return RedirectToAction(nameof(Offering), new { storeId, offeringId });
    }

    private static void UpdateEntitlements(ApplicationDbContext ctx, OfferingData offering, ConfigureOfferingViewModel vm)
    {
        var incomingById = vm.Entitlements
            .GroupBy(e => e.Id)                           // guard against dupes
            .ToDictionary(g => g.Key, g => g.First());

        var existingById = offering.Entitlements
            .ToDictionary(e => e.CustomId);


        var toRemove = offering.Entitlements
            .Where(e => !incomingById.ContainsKey(e.CustomId))
            .ToList();

        foreach (var e in toRemove)
            offering.Entitlements.Remove(e);

        ctx.Entitlements.RemoveRange(toRemove);

        // Update or add entitlements
        foreach (var (id, vmEnt) in incomingById)
        {
            if (!existingById.TryGetValue(id, out var entity))
            {
                entity = new();
                entity.CustomId = vmEnt.Id;
                entity.OfferingId = offering.Id;
                offering.Entitlements.Add(entity);
            }

            entity.Name = vmEnt.Name;
            entity.Description = vmEnt.ShortDescription;
        }
    }

    [HttpGet("stores/{storeId}/offerings/{offeringId}/add-plan")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> AddPlan(string storeId, string offeringId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var offering = await GetOfferingData(ctx, storeId, offeringId);
        return View(new AddEditPlanViewModel()
        {
            OfferingId = offeringId,
            OfferingName = offering?.App.Name ?? "",
            Currency = this.HttpContext.GetStoreData().GetStoreBlob().DefaultCurrency,
            Entitlements = offering.Entitlements.Select(e => new AddEditPlanViewModel.Entitlement()
            {
                CustomId = e.CustomId,
                Name = e.Name,
                Quantity = 0,
                ShortDescription = e.Description
            }).ToList()
        });
    }
    [HttpPost("stores/{storeId}/offerings/{offeringId}/add-plan")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> AddPlan(string storeId, string offeringId, AddEditPlanViewModel vm, string? command = null, int? removeIndex = null)
    {
        if (!ModelState.IsValid)
            return await AddPlan(storeId, offeringId);
        await using var ctx = dbContextFactory.CreateContext();
        var offering = await GetOfferingData(ctx, storeId, offeringId);
        // Check if the offering is part of the store
        if (offering is null)
            return NotFound();
        var plan = new PlanData()
        {
            Name = vm.Name,
            Description = vm.Description,
            Price = vm.Price,
            Currency = vm.Currency,
            GracePeriodDays = vm.GracePeriodDays,
            TrialDays = vm.TrialDays,
            AllowUpgrade = vm.AllowUpgrade,
            OptimisticActivation = vm.OptimisticActivation,
            CreatedAt = DateTimeOffset.UtcNow,
            RecurringType = vm.RecurringType,
            Status = vm.Status,
            OfferingId = vm.OfferingId,
            PlanEntitlements = new()
        };

        var entitlementsById = vm.Entitlements.ToDictionary(o => o.CustomId);
        foreach (var entitlement in offering.Entitlements)
        {
            var pe = new PlanEntitlementData()
            {
                PlanId = plan.Id,
                Quantity = entitlementsById[entitlement.CustomId].Quantity,
                EntitlementId = entitlement.Id
            };
            ctx.PlanEntitlements.Add(pe);
            plan.PlanEntitlements.Add(pe);
        }
        ctx.Plans.Add(plan);
        await ctx.SaveChangesAsync();
        this.TempData.SetStatusSuccess(StringLocalizer["New plan created"]);
        return RedirectToAction(nameof(Offering), new { storeId = plan.Offering.App.StoreDataId, offeringId = plan.OfferingId });
    }

    private static async Task<OfferingData?> GetOfferingData(ApplicationDbContext ctx, string storeId, string offeringId)
    {
        var offering = await ctx.Offerings
            .Include(o => o.Entitlements)
            .Include(o => o.App)
            .ThenInclude(o => o.StoreData)
            .Where(o => o.App.StoreDataId == storeId && o.Id == offeringId)
            .FirstOrDefaultAsync();
        return offering;
    }
    private static string GenerateEntitlementId(ConfigureOfferingViewModel.EntitlementViewModel o) => Regex.Replace(o.Name.ToLowerInvariant().Trim(), @"\s", "-");
}
