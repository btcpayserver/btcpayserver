#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
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
    public async Task<IActionResult> PlanCheckout(string planId, PlanCheckoutViewModel vm, string? prefilledEmail = null)
    {
        if (!vm.Email.IsValidEmail())
            ModelState.AddModelError(nameof(vm.Email), "Invalid email format");
        if (!ModelState.IsValid)
            return await PlanCheckout(planId, prefilledEmail);
        await using var ctx = dbContextFactory.CreateContext();
        var plan = await GetPlanFromId(planId, ctx);
        if (plan is null)
            return NotFound();

        var metadata = new InvoiceMetadata()
        {
            BuyerEmail = vm.Email,
        }.ToJObject();
        metadata.Add("planId", plan.Id);
        var request = await invoiceController.CreateInvoiceCoreRaw(new ()
        {
            Currency = plan.Currency,
            Amount = plan.Price,
            Metadata = metadata
        }, plan.Offering.App.StoreData, Request.GetAbsoluteRoot(),
            [ GetPlanInvoiceTag(plan.Id) ]);

        var link = callbackGenerator.InvoiceCheckoutLink(request.Id, Request);
        return Redirect(link);
    }

    public static string GetPlanInvoiceTag(string planId) => $"SUBS#{planId}";
    public static string? GetPlanIdFromInvoice(InvoiceEntity invoiceEntiy) => invoiceEntiy.GetInternalTags("SUBS#").FirstOrDefault();

    [HttpGet("stores/{storeId}/offerings")]
    public IActionResult CreateOffering(string storeId)
    {
        return View();
    }

    public IActionResult ConfigureOffering(string storeId, string offeringId)
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
            Html = StringLocalizer["New offering created. <a href='{0}'>Click here to configure it.</a>", Url.Action(nameof(ConfigureOffering), new { storeId, offeringId = o.Id })!],
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(Offering), new{ storeId, offeringId = o.Id });
    }

    [HttpGet("stores/{storeId}/offerings/{offeringId}/{section=Plans}")]
    public async Task<IActionResult> Offering(string storeId, string offeringId, SubscriptionSection section = SubscriptionSection.Plans)
    {
        await using var ctx = dbContextFactory.CreateContext();

        var vm = new SubscriptionsViewModel() { Section = section };

        // TODO: This shouldn't be a property of the store, but one of the membership
        vm.Currency = this.HttpContext.GetStoreData().GetStoreBlob().DefaultCurrency;

        if (section == SubscriptionSection.Plans)
        {
            var plans = await ctx.Plans
                .Where(p => p.OfferingId == offeringId)
                .ToListAsync();

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

    [HttpGet("stores/{storeId}/offerings/{offeringId}/add-plan")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult AddPlan(string storeId, string offeringId)
    {
        return View(new AddEditMembershipPlanViewModel()
        {
            OfferingId = offeringId,
            Currency = this.HttpContext.GetStoreData().GetStoreBlob().DefaultCurrency
        });
    }
    [HttpPost("stores/{storeId}/offerings/{offeringId}/add-plan")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> AddPlan(AddEditMembershipPlanViewModel vm, string? command = null, int? removeIndex = null)
    {
        // bool itemsUpdated = false;
        // if (command == "AddItem")
        // {
        //     vm.Items ??= new();
        //     vm.Items.Add(new());
        //     itemsUpdated = true;
        // }
        // else if (removeIndex is int i)
        // {
        //     vm.Items.RemoveAt(i);
        //     itemsUpdated = true;
        // }
        // if (itemsUpdated)
        // {
        //     this.ModelState.Clear();
        //     vm.Anchor = "plan-items";
        // }
        // if (!ModelState.IsValid || itemsUpdated)
        //     return View(vm);

        if (!ModelState.IsValid)
            return View(vm);

        var storeId = HttpContext.GetCurrentStoreId();
        await using var ctx = dbContextFactory.CreateContext();
        var offering = await ctx.Offerings
                                .Include(o => o.Entitlements)
                                .Include(o => o.App)
                                .Where(o => o.App.StoreDataId == storeId && o.Id == vm.OfferingId)
                                .FirstOrDefaultAsync();
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


    // private static string GeneratePlanItemId(AddEditMembershipPlanViewModel.Entitlements o) => Regex.Replace(o.Name.ToLowerInvariant().Trim(), @"\s", "-");
}
