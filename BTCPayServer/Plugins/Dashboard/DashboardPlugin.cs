using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Dashboard.Models;
using BTCPayServer.Plugins.Dashboard.Widgets;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Dashboard;

public class DashboardPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "BTCPayServer.Plugins.Dashboard";
    public override string Name => "Dashboard";
    public override string Description => "Customizable Blazor dashboard with widget framework.";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<WidgetRegistry>();
        services.AddScoped<DashboardService>();
        services.AddScoped<DashboardJsInterop>();

        services.AddSingleton<IDashboardTemplateProvider, DefaultStoreDashboardTemplate>();
        services.AddSingleton<IDashboardTemplateProvider, DefaultServerDashboardTemplate>();

        // Single demonstration widget for the skeleton PR.
        // The remaining widgets land in the follow-up PR.
        services.AddDashboardWidget<NotesWidget>(NotesWidget.Descriptor);
    }
}

public static class DashboardServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Blazor widget component for the customizable dashboard. Plugins
    /// extending the dashboard call this from their own <c>Execute</c> method to
    /// contribute additional widget types.
    /// </summary>
    public static IServiceCollection AddDashboardWidget<TComponent>(
        this IServiceCollection services, WidgetDescriptor descriptor)
        where TComponent : ComponentBase
    {
        descriptor.ComponentType = typeof(TComponent);
        services.AddSingleton(descriptor);
        return services;
    }
}
