using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Views.UIStoreMembership;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Controllers;

public partial class UIStoreSubscriptionsController
{
private async Task<IActionResult> CreateFakeOffering(string storeId, CreateOfferingViewModel vm)
    {
        ModelState.Clear();
        var redirect = (RedirectToActionResult)await CreateOffering(storeId, vm);
        var offeringId = (string)redirect.RouteValues!["offeringId"]!;
        await using var ctx = dbContextFactory.CreateContext();
        var offering = await ctx.Offerings
            .Include(o => o.Plans)
            .Include(o => o.App)
            .Where(o => o.Id == offeringId)
            .FirstAsync();
        offering.App.Name = vm.Name ?? "PayFlow Pro";
        foreach (var e in new[]
                 {
                     ("Up to X transactions/month", "Transaction Limit", "transaction-limit"),
                     ("Basic payment processing", "Payment Processing", "payment-processing"),
                     ("Email support", "Support", "email-support"),
                     ("Standard security features", "Security", "security-features"),
                     ("Basic analytics dashboard", "Analytics", "analytics-dashboard")
                 })
        {
            ctx.Entitlements.Add(new()
            {
                OfferingId = offering.Id,
                Description = e.Item1,
                Name = e.Item2,
                CustomId = e.Item3
            });
        }

        await ctx.SaveChangesAsync();
        var plans = new List<PlanData>();
        var entitlements = await ctx.Entitlements.Where(c => c.OfferingId == offeringId).ToDictionaryAsync(x => x.CustomId);

        var p = ctx.Plans.Add(new()
        {
            Name = "Basic Plan",
            Description = "Perfect for small businesses getting started",
            Price = 29.0m,
            Currency = "USD",
            TrialDays = 7,
            OfferingId = offering.Id,
            Status = PlanData.PlanStatus.Active
        });

        foreach (var e in new[]
                 {
                     ("transaction-limit", "Up to 10,000 transactions/month", 10000m),
                     ("payment-processing", null, 1),
                     ("email-support", null, 1),
                     ("security-features", null, 1),
                     ("analytics-dashboard", null, 1)
                 })
        {
            ctx.PlanEntitlements.Add(new()
            {
                PlanId = p.Entity.Id,
                Description = e.Item2,
                Quantity = e.Item3,
                EntitlementId = entitlements[e.Item1].Id
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

        foreach (var e in new[]
                 {
                     ("transaction-limit", "Up to 50,000 transactions/month", 50000m),
                     ("payment-processing", "Advanced payment processing", 1),
                     ("email-support", "Priority email support", 2),
                     ("security-features", "Enhanced security features", 2),
                     ("analytics-dashboard", "Advanced analytics", 1)
                 })
        {
            ctx.PlanEntitlements.Add(new()
            {
                PlanId = p.Entity.Id,
                Description = e.Item2,
                Quantity = e.Item3,
                EntitlementId = entitlements[e.Item1].Id
            });
        }

        p = ctx.Plans.Add(new()
        {
            Name = "Enterprise Plan",
            Description = "For large scale operations",
            Price = 299.0m,
            Currency = "USD",
            TrialDays = 30,
            OfferingId = offering.Id,
            Status = PlanData.PlanStatus.Active
        });

        foreach (var e in new[]
                 {
                     ("transaction-limit", "Unlimited transactions", 1000000m),
                     ("payment-processing", "Enterprise payment processing", 1),
                     ("email-support", "24/7 dedicated support", 3),
                     ("security-features", "Enterprise security suite", 3),
                     ("analytics-dashboard", "Custom analytics & reporting", 1)
                 })
        {
            ctx.PlanEntitlements.Add(new()
            {
                PlanId = p.Entity.Id,
                Description = e.Item2,
                Quantity = e.Item3,
                EntitlementId = entitlements[e.Item1].Id
            });
        }
        await ctx.SaveChangesAsync();

        return redirect;
    }
}
