using BlazorDashboardKit;
using BlazorDashboardKit.Abstractions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Dashboard.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Dashboard;

public class DashboardPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "BTCPayServer.Plugins.Dashboard";
    public override string Name => "Dashboard";
    public override string Description => "Customizable Blazor dashboard (BlazorDashboardKit).";

    public override void Execute(IServiceCollection services)
    {
        // Register the BTCPay adapters BEFORE AddBlazorDashboard(): the kit uses
        // TryAdd* for IDashboardStore/IWidgetAccessControl, so ours must already
        // be present to win over its in-memory / allow-all defaults.
        services.AddSingleton<IDashboardStore, BtcpayDashboardStore>();
        services.AddSingleton<IWidgetAccessControl, BtcpayWidgetAccessControl>();

        services.AddBlazorDashboard();

        services.AddDashboardWidget<NotesWidget>(NotesWidget.Descriptor);
    }
}
