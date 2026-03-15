#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BTCPayServer.Blazor.Dashboard.Models;

namespace BTCPayServer.Blazor.Dashboard;

public class WidgetRegistry
{
    public ImmutableArray<WidgetDescriptor> Descriptors { get; }

    public WidgetRegistry(IEnumerable<WidgetDescriptor> descriptors)
    {
        Descriptors = descriptors.ToImmutableArray();
    }

    public WidgetDescriptor? GetDescriptor(string type)
        => Descriptors.FirstOrDefault(d => string.Equals(d.Type, type, StringComparison.Ordinal));

    public IEnumerable<WidgetDescriptor> GetAvailableFor(DashboardScope dashboardScope, bool isAdmin = false)
    {
        // The dashboard page always has a store context (from the URL), so store-scoped
        // widgets are available on all dashboard scopes — the scope of a dashboard only
        // determines where it's persisted, not what data it can access.
        return Descriptors.Where(d =>
        {
            return d.Scope switch
            {
                WidgetScope.Universal => true,
                WidgetScope.Store => true,
                WidgetScope.Server => isAdmin,
                WidgetScope.User => true,
                _ => false
            };
        });
    }
}
