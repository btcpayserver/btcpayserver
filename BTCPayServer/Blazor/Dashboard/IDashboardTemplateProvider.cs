#nullable enable
using System.Collections.Generic;
using BTCPayServer.Blazor.Dashboard.Models;
using BTCPayServer.Data;

namespace BTCPayServer.Blazor.Dashboard;

public class DashboardTemplateContext
{
    public bool WalletEnabled { get; set; }
    public bool LightningEnabled { get; set; }
    public List<AppData> Apps { get; set; } = new();
    public string CryptoCode { get; set; } = "BTC";
}

public interface IDashboardTemplateProvider
{
    string Name { get; }
    DashboardScope Scope { get; }
    DashboardDefinition GetTemplate(DashboardTemplateContext context);
}

/// <summary>
/// Plugins implement this to contribute widgets to default dashboard templates.
/// Contributions are appended to the end of the template when a new dashboard
/// is created or when the user resets to the default layout.
/// Existing saved dashboards are never modified.
/// </summary>
public interface IDashboardWidgetContributor
{
    /// <summary>Which dashboard scopes this contributor applies to.</summary>
    IEnumerable<DashboardScope> ApplicableScopes { get; }

    /// <summary>
    /// Returns widget placements to append to the default template.
    /// The Order values will be adjusted automatically to follow existing widgets.
    /// </summary>
    IEnumerable<WidgetPlacement> GetWidgets(DashboardScope scope, DashboardTemplateContext context);
}
