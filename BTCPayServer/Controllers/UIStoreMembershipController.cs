#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
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
    IStringLocalizer StringLocalizer
    ) : Controller
{
    [HttpGet("stores/{storeId}/membership/{section=Plans}")]
    public async Task<IActionResult> Membership(string storeId, MembershipSection section = MembershipSection.Plans)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var vm = new MembershipViewModel() { Section = section };
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

        // TODO: Look at the AdditionalData of the plan for cached value of number of users

        vm.Plans = plans.Select(p =>
            new MembershipViewModel.PlanViewModel()
            {
                Data = p,
                UserCount = 0
            }).ToList();
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
            CreatedAt = DateTimeOffset.UtcNow,
            Id = SubscriptionPlanData.GenerateId(),
            RecurringType = vm.RecurringType,
            Status = vm.Status,
            StoreId = this.HttpContext.GetCurrentStoreId()
        };
        if (vm.Items?.Count is > 0)
        {
            var data = plan.GetAdditionalData<SubscriptionPlanData.BTCPayAdditionalData>();
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
            plan.SetAdditionalData(data);
        }
        ctx.SubscriptionPlans.Add(plan);
        await ctx.SaveChangesAsync();
        this.TempData.SetStatusSuccess(StringLocalizer["New plan created"]);
        return RedirectToAction(nameof(Membership), new { storeId = plan.StoreId });
    }


    private static string GeneratePlanItemId(AddEditMembershipPlanViewModel.PlanItemInput o) => Regex.Replace(o.Name.ToLowerInvariant().Trim(), @"\s", "-");
}
