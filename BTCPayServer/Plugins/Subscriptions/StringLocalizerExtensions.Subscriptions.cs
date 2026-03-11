using System;
using BTCPayServer.Data.Subscriptions;
using Microsoft.Extensions.Localization;
// ReSharper disable InconsistentNaming

namespace BTCPayServer.Plugins.Subscriptions;

public static class StringLocalizerExtensions
{
    public static string RecurringToString(this IStringLocalizer StringLocalizer, PlanData.RecurringInterval type)
        => (type switch
        {
            PlanData.RecurringInterval.Lifetime => StringLocalizer["for lifetime"],
            PlanData.RecurringInterval.Monthly => StringLocalizer["per month"],
            PlanData.RecurringInterval.Quarterly => StringLocalizer["per quarter"],
            PlanData.RecurringInterval.Yearly => StringLocalizer["per year"],
            _ => throw new NotSupportedException(type.ToString())
        }).Value;
}
