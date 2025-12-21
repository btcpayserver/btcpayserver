#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Views.UIStoreMembership;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Subscriptions.Controllers;

public partial class UIOfferingController
{
    private async Task<IActionResult> CreateFakeOffering(string storeId, CreateOfferingViewModel vm)
    {
        ModelState.Clear();
        var redirect = (RedirectToActionResult)await CreateOffering(storeId, vm);
        var offeringId = (string)redirect.RouteValues!["offeringId"]!;
        await using var ctx = DbContextFactory.CreateContext();
        var offering = await ctx.Offerings
            .Include(o => o.Plans)
            .Include(o => o.App)
            .Where(o => o.Id == offeringId)
            .FirstAsync();
        offering.App.Name = vm.Name ?? "PayFlow Pro";
        foreach (var e in new[]
                 {
                     ("Up to 10,000 transactions/month", "transaction-limit-10000"),
                     ("Up to 50,000 transactions/month", "transaction-limit-50000"),
                     ("Unlimited transactions", "transaction-limit-x"),

                     ("Basic payment processing", "payment-processing-0"),
                     ("Advanced payment processing", "payment-processing-1"),
                     ("Enterprise payment processing", "payment-processing-x"),

                     ("Email support", "email-support-0"),
                     ("Priority Email support", "email-support-1"),
                     ("24/7 dedicated support", "email-support-x"),


                     ("Standard security features", "security-features-0"),
                     ("Enhanced security suite", "security-features-1"),
                     ("Enterprise security suite", "security-features-x"),

                     ("Basic analytics dashboard", "analytics-dashboard-0"),
                     ("Advanced analytics", "analytics-dashboard-1"),
                     ("Custom analytics & reporting", "analytics-dashboard-x"),
                 })
        {
            ctx.Features.Add(new()
            {
                OfferingId = offering.Id,
                Description = e.Item1,
                CustomId = e.Item2
            });
        }

        await ctx.SaveChangesAsync();
        var features = await ctx.Features.Where(c => c.OfferingId == offeringId).ToDictionaryAsync(x => x.CustomId);

        var p = ctx.Plans.Add(new()
        {
            Name = "Basic Plan",
            Description = "Perfect for small businesses getting started",
            Price = 29.0m,
            Currency = "USD",
            TrialDays = 0,
            OfferingId = offering.Id,
            Status = PlanData.PlanStatus.Active
        });
        var basicPlan = p;

        foreach (var e in new[]
                 {
                     "transaction-limit-10000",
                     "payment-processing-0",
                     "email-support-0",
                     "security-features-0",
                     "analytics-dashboard-0"
                 })
        {
            ctx.PlanFeatures.Add(new()
            {
                PlanId = p.Entity.Id,
                FeatureId = features[e].Id
            });
        }

        p = ctx.Plans.Add(new()
        {
            Name = "Pro Plan",
            Description = "Great for growing businesses",
            Price = 99.0m,
            Currency = "USD",
            TrialDays = 14,
            OfferingId = offering.Id,
            Status = PlanData.PlanStatus.Active
        });
        var proPlan = p;

        foreach (var e in new[]
                 {
                     "transaction-limit-50000",
                     "payment-processing-1",
                     "email-support-1",
                     "security-features-1",
                     "analytics-dashboard-1"
                 })
        {
            ctx.PlanFeatures.Add(new()
            {
                PlanId = p.Entity.Id,
                FeatureId = features[e].Id
            });
        }

        p = ctx.Plans.Add(new()
        {
            Name = "Enterprise Plan",
            Description = "For large scale operations",
            Price = 299.0m,
            Currency = "USD",
            TrialDays = 15,
            GracePeriodDays = 15,
            OfferingId = offering.Id,
            Status = PlanData.PlanStatus.Active
        });
        var enterprisePlan = p;

        foreach (var e in new[]
                 {
                     "transaction-limit-x",
                     "payment-processing-x",
                     "email-support-x",
                     "security-features-x",
                     "analytics-dashboard-x"
                 })
        {
            ctx.PlanFeatures.Add(new()
            {
                PlanId = p.Entity.Id,
                FeatureId = features[e].Id
            });
        }

        ctx.PlanChanges.Add(new()
        {
            PlanId = basicPlan.Entity.Id,
            PlanChangeId = proPlan.Entity.Id,
            Type = PlanChangeData.ChangeType.Upgrade
        });
        ctx.PlanChanges.Add(new()
        {
            PlanId = basicPlan.Entity.Id,
            PlanChangeId = enterprisePlan.Entity.Id,
            Type = PlanChangeData.ChangeType.Upgrade
        });

        ctx.PlanChanges.Add(new()
        {
            PlanId = proPlan.Entity.Id,
            PlanChangeId = basicPlan.Entity.Id,
            Type = PlanChangeData.ChangeType.Downgrade
        });
        ctx.PlanChanges.Add(new()
        {
            PlanId = proPlan.Entity.Id,
            PlanChangeId = enterprisePlan.Entity.Id,
            Type = PlanChangeData.ChangeType.Upgrade
        });

        ctx.PlanChanges.Add(new()
        {
            PlanId = enterprisePlan.Entity.Id,
            PlanChangeId = basicPlan.Entity.Id,
            Type = PlanChangeData.ChangeType.Downgrade
        });
        ctx.PlanChanges.Add(new()
        {
            PlanId = enterprisePlan.Entity.Id,
            PlanChangeId = proPlan.Entity.Id,
            Type = PlanChangeData.ChangeType.Downgrade
        });

        await ctx.SaveChangesAsync();

        return redirect;
    }
}
