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
        //
        // Scoped (not singleton): BtcpayWidgetAccessControl depends on the scoped
        // IAuthorizationService — a singleton consuming it is a captive dependency
        // that fails BTCPay's scope validation at startup. The kit only resolves
        // these via its scoped DashboardService / component context, so scoped is
        // both correct and safe (scoped can consume scoped + singleton deps).
        services.AddScoped<IDashboardStore, BtcpayDashboardStore>();
        services.AddScoped<IWidgetAccessControl, BtcpayWidgetAccessControl>();

        services.AddBlazorDashboard();

        services.AddDashboardWidget<NotesWidget>(NotesWidget.Descriptor);
    }
}
