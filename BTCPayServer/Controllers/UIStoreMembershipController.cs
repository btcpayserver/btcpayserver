#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Services;
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
public class UIStoreMembershipController(
    ApplicationDbContextFactory dbContextFactory,
    IStringLocalizer StringLocalizer,
    UriResolver uriResolver,
    UIInvoiceController invoiceController,
    CallbackGenerator callbackGenerator
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
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, plan.Store.GetStoreBlob()),
            StoreName = plan.Store.StoreName,
            Title = plan.Name,
            Data = plan,
            Email = prefilledEmail,
            IsPrefilled = prefilledEmail?.IsValidEmail() is true,
        };
        return View(vm);
    }

    private static async Task<SubscriptionPlanData?> GetPlanFromId(string planId, ApplicationDbContext ctx)
    {
        var plan = await ctx.SubscriptionPlans
            .Include(o => o.Store)
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
        }, plan.Store, Request.GetAbsoluteRoot(),
            [ GetPlanInvoiceTag(plan.Id) ]);

        var link = callbackGenerator.InvoiceCheckoutLink(request.Id, Request);
        return Redirect(link);
    }

    public static string GetPlanInvoiceTag(string planId) => $"SUBS#{planId}";
    public static string? GetPlanIdFromInvoice(InvoiceEntity invoiceEntiy) => invoiceEntiy.GetInternalTags("SUBS#").FirstOrDefault();

    [HttpGet("stores/{storeId}/membership/{section=Plans}")]
    public async Task<IActionResult> Membership(string storeId, MembershipSection section = MembershipSection.Plans)
    {
        await using var ctx = dbContextFactory.CreateContext();

        var vm = new MembershipViewModel() { Section = section };
        vm.Stats = await ctx.SubscriptionStats
            .Where(s => s.StoreId == storeId)
            .FirstOrDefaultAsync() ?? new();

        // TODO: This shouldn't be a property of the store, but one of the membership
        vm.Currency = this.HttpContext.GetStoreData().GetStoreBlob().DefaultCurrency;

        if (section == MembershipSection.Plans)
        {
            var plans = await ctx.SubscriptionPlans
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            plans = plans
                .OrderBy(p => p.Status switch
                {
                    SubscriptionPlanData.PlanStatus.Active => 0,
                    SubscriptionPlanData.PlanStatus.Draft => 1,
                    _ => 2,
                })
                .ThenByDescending(o => o.CreatedAt)
                .ToList();

            vm.Plans = plans.Select(p =>
                new MembershipViewModel.PlanViewModel()
                {
                    Data = p
                }).ToList();
        }
        else if (section == MembershipSection.Members)
        {
            var members = ctx.Members
                .Include(m => m.Plan)
                .Include(m => m.Customer)
                .Where(m => m.Customer.StoreId == storeId)
                .ToList();
            vm.Members = members
                .Select(v => new MembershipViewModel.MemberViewModel()
                {
                    Data = v
                }).ToList();
        }

        return View(vm);
    }

    [HttpGet("stores/{storeId}/membership/add-plan")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult AddMembershipPlan()
    {
        return View(new AddEditMembershipPlanViewModel()
        {
            Currency = this.HttpContext.GetStoreData().GetStoreBlob().DefaultCurrency
        });
    }
    [HttpPost("stores/{storeId}/membership/add-plan")]
    [Authorize(Policy = Policies.CanModifyMembership, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> AddMembershipPlan(AddEditMembershipPlanViewModel vm, string? command = null, int? removeIndex = null)
    {
        bool itemsUpdated = false;
        if (command == "AddItem")
        {
            vm.Items ??= new();
            vm.Items.Add(new());
            itemsUpdated = true;
        }
        else if (removeIndex is int i)
        {
            vm.Items.RemoveAt(i);
            itemsUpdated = true;
        }
        if (itemsUpdated)
        {
            this.ModelState.Clear();
            vm.Anchor = "plan-items";
        }
        if (!ModelState.IsValid || itemsUpdated)
            return View(vm);


        await using var ctx = dbContextFactory.CreateContext();
        var plan = new SubscriptionPlanData
        {
            Name = vm.Name,
            Description = vm.Description,
            Price = vm.Price,
            Currency = vm.Currency,
            GracePeriodDays = vm.GracePeriodDays,
            AllowUpgrade = vm.AllowUpgrade,
            OptimisticActivation = vm.OptimisticActivation,
            CreatedAt = DateTimeOffset.UtcNow,
            Id = SubscriptionPlanData.GenerateId(),
            RecurringType = vm.RecurringType,
            Status = vm.Status,
            StoreId = this.HttpContext.GetCurrentStoreId()
        };
        if (vm.Items?.Count is > 0)
        {
            var data = plan.GetBlob() ?? new();
            data.PlanItems = vm.Items
                .Select(o =>
                new SubscriptionPlanData.SubscriptionPlanItem()
                {
                    Name = o.Name,
                    Quantity = o.Quantity,
                    ShortDescription = o.ShortDescription,
                    Id = o.Id ?? GeneratePlanItemId(o)
                }).ToList();
            if (data.HasDuplicateIds(ModelState, "Items[{0}].Id", StringLocalizer["Duplicate ID"]))
                return View(vm);
            plan.SetAdditionalData(SubscriptionPlanData.BTCPayAdditionalData.Key, data);
        }
        ctx.SubscriptionPlans.Add(plan);
        await ctx.SaveChangesAsync();
        this.TempData.SetStatusSuccess(StringLocalizer["New plan created"]);
        return RedirectToAction(nameof(Membership), new { storeId = plan.StoreId });
    }


    private static string GeneratePlanItemId(AddEditMembershipPlanViewModel.PlanItemInput o) => Regex.Replace(o.Name.ToLowerInvariant().Trim(), @"\s", "-");
}
