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
