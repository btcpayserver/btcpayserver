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
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIStoreSubscriptionsController(
    ApplicationDbContextFactory dbContextFactory,
    IStringLocalizer StringLocalizer,
    UriResolver uriResolver,
    CallbackGenerator callbackGenerator,
    EventAggregator eventAggregator,
    SubscriptionHostedService subsService,
    AppService appService
) : Controller
{
    [HttpGet("plan-checkout/{checkoutId}")]
    [AllowAnonymous]
    public async Task<IActionResult> PlanCheckout(string checkoutId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutId);
        var plan = checkout?.Plan;
        if (plan is null || checkout is null)
            return NotFound();
        var prefilledEmail = GetInvoiceMetadata(checkout).BuyerEmail;
        var vm = new PlanCheckoutViewModel()
        {
            Id = plan.Id,
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

    private static InvoiceMetadata GetInvoiceMetadata(PlanCheckoutData checkout)
    {
        return InvoiceMetadata.FromJObject(JObject.Parse(checkout.InvoiceMetadata));
    }

    [HttpGet("plan-checkout/default-redirect")]
    [AllowAnonymous]
    public async Task<IActionResult> PlanCheckoutDefaultRedirect(string? checkoutId = null)
    {
        if (checkoutId is null)
            return NotFound();
        await using var ctx = dbContextFactory.CreateContext();
        var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutId);
        if (checkout is null)
            return NotFound();

        return View(new PlanCheckoutDefaultRedirectViewModel(checkout)
        {
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, checkout.Plan.Offering.App.StoreData.GetStoreBlob()),
            StoreName = checkout.Plan.Offering.App.StoreData.StoreName,
        });
    }

    [HttpPost("plan-checkout/{checkoutId}")]
    [AllowAnonymous]
    public async Task<IActionResult> PlanCheckout(string checkoutId, PlanCheckoutViewModel vm, string? command = null,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutId);
        if (checkout is null)
            return NotFound();
        var checkoutInvoice = checkout.InvoiceId is null ? null : await ctx.Invoices.FindAsync([checkout.InvoiceId], cancellationToken);
        if (checkoutInvoice is not null)
        {
            var status = checkoutInvoice.GetInvoiceState().Status;

            if (status is InvoiceStatus.Settled && checkout.GetRedirectUrl() is string url)
                return Redirect(url);
            if (status is not (InvoiceStatus.Expired or InvoiceStatus.Invalid))
                return RedirectToInvoiceCheckout(checkoutInvoice.Id);
        }

        var invoiceMetadata = GetInvoiceMetadata(checkout);
        if (invoiceMetadata.BuyerEmail is not null)
            vm.Email = invoiceMetadata.BuyerEmail;
        var customerSelector = CustomerSelector.ByEmail(vm.Email);
        if (!vm.Email.IsValidEmail())
            ModelState.AddModelError(nameof(vm.Email), "Invalid email format");
        if (!ModelState.IsValid)
            return await PlanCheckout(checkoutId);
        if (checkout.NewSubscriber)
        {
            var sub = await ctx.Subscribers.GetBySelector(checkout.Plan.OfferingId, customerSelector);
            if (sub is not null)
            {
                ModelState.AddModelError(nameof(vm.Email), "This email already has a subscription to this offering");
                return await PlanCheckout(checkoutId);
            }
        }

        if (invoiceMetadata.BuyerEmail is null)
        {
            invoiceMetadata.BuyerEmail = vm.Email;
            checkout.InvoiceMetadata = invoiceMetadata.ToJObject().ToString();
            await ctx.SaveChangesAsync(cancellationToken);
        }

        try
        {
            await subsService.ProceedToSubscribe(checkout.Id, Request.GetRequestBaseUrl(), customerSelector, cancellationToken);
        }
        catch (BitpayHttpException ex)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Html = ex.Message.Replace("\n", "<br />", StringComparison.OrdinalIgnoreCase),
                Severity = StatusMessageModel.StatusSeverity.Error,
                AllowDismiss = true
            });
            return await PlanCheckout(checkoutId);
        }

        await ctx.Entry(checkout).ReloadAsync(cancellationToken);
        if (checkout.InvoiceId != null)
        {
            return RedirectToInvoiceCheckout(checkout.InvoiceId);
        }
        else if (checkout.IsTrial && checkout.GetRedirectUrl() is string url)
        {
            return Redirect(url);
        }
        else
        {
            return NotFound();
        }
    }

    private RedirectResult RedirectToInvoiceCheckout(string invoiceId) => Redirect(callbackGenerator.InvoiceCheckoutLink(invoiceId, Request));


    [HttpGet("new-plan-checkout/{planId}")]
    [AllowAnonymous]
    public async Task<IActionResult> NewPlanCheckout(string planId, string? prefilledEmail = null)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var plan = await ctx.Plans.GetPlanFromId(planId);
        if (plan is null)
            return NotFound();

        var checkoutData = new PlanCheckoutData()
        {
            PlanId = planId,
            IsTrial = plan.TrialDays > 0,
            NewSubscriber = true
        };

        if (prefilledEmail != null)
            checkoutData.InvoiceMetadata = new InvoiceMetadata() { BuyerEmail = prefilledEmail }.ToJObject().ToString();

        ctx.PlanCheckouts.Add(checkoutData);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(PlanCheckout), new { checkoutId = checkoutData.Id });
    }

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
            Html = StringLocalizer["New offering created. You can now <a href='{0}' class='alert-link'>configure it.</a>",
                Url.Action(nameof(ConfigureOffering), new { storeId, offeringId = o.Id })!],
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(Offering), new { storeId, offeringId = o.Id });
    }

    [HttpGet("stores/{storeId}/offerings/{offeringId}/{section=Plans}")]
    public async Task<IActionResult> Offering(string storeId, string offeringId, SubscriptionSection section = SubscriptionSection.Plans)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
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
                .Include(m => m.Customer).ThenInclude(c => c.CustomerIdentities)
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
        var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
        if (offering is null)
            return NotFound();

        offering.SuccessRedirectUrl = vm.SuccessRedirectUrl;
        offering.App.Name = vm.Name;
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

            entity.Name = vmEnt.Name;
            entity.Description = vmEnt.ShortDescription;
        }
    }

    [HttpGet("stores/{storeId}/offerings/{offeringId}/add-plan")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> AddPlan(string storeId, string offeringId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
        if (offering is null)
            return NotFound();
        return View(new AddEditPlanViewModel()
        {
            OfferingId = offeringId,
            OfferingName = offering.App.Name,
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
        var offering = await ctx.Offerings.GetOfferingData(offeringId, storeId);
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

    private static string GenerateEntitlementId(ConfigureOfferingViewModel.EntitlementViewModel o) =>
        Regex.Replace(o.Name.ToLowerInvariant().Trim(), @"\s", "-");
}
