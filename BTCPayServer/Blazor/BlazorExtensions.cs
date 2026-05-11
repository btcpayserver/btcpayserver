using BTCPayServer.Blazor.Dashboard;
using BTCPayServer.Blazor.Dashboard.Widgets;
using BTCPayServer.Blazor.Dashboard.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace BTCPayServer.Blazor
{
    public static class BlazorExtensions
    {
        public static bool IsPreRendering(this IJSRuntime runtime)
        {
            // The peculiar thing in prerender is that Blazor circuit isn't yet created, so we can't use JSInterop
            return !(bool)runtime.GetType().GetProperty("IsInitialized").GetValue(runtime);
        }

        public static IServiceCollection AddDashboardServices(this IServiceCollection services)
        {
            services.AddSingleton<WidgetRegistry>();
            services.AddScoped<DashboardService>();
            services.AddScoped<DashboardJsInterop>();

            services.AddSingleton<IDashboardTemplateProvider, DefaultStoreDashboardTemplate>();
            services.AddSingleton<IDashboardTemplateProvider, DefaultServerDashboardTemplate>();

            // Single demonstration widget for the skeleton PR.
            // The remaining widgets land in the follow-up PR.
            services.AddDashboardWidget<NotesWidget>(NotesWidget.Descriptor);

            return services;
        }

        public static IServiceCollection AddDashboardWidget<TComponent>(
            this IServiceCollection services, WidgetDescriptor descriptor)
            where TComponent : ComponentBase
        {
            descriptor.ComponentType = typeof(TComponent);
            services.AddSingleton(descriptor);
            return services;
        }
    }
}
