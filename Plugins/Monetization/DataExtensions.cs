#nullable enable

using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;

namespace BTCPayServer.Plugins.Monetization;

public static class SubscriberDataExtensions
{
    public const string IdentityType = "ApplicationUserId";
    public static string? GetApplicationUserId(this SubscriberData subscriber)
        => subscriber.Customer.GetContact(IdentityType);
}

public static class DataExtensions
{
    public static async Task<(OfferingData Offering, PlanData Plan)?> GetOfferingAndPlan(this ApplicationDbContext ctx, MonetizationSettings? settings)
    {
        if (settings is null)
            return null;
        var offering = await ctx.Offerings.GetOfferingData(settings.OfferingId ?? "");
        if (offering is null)
            return null;
        var plan = await ctx.Plans.GetPlanFromId(settings.DefaultPlanId ?? "");
        if (plan is null || plan.OfferingId != offering.Id)
            return null;
        return (offering, plan);
    }
}

