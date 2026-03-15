using BTCPayServer.Blazor.Dashboard;
using BTCPayServer.Blazor.Dashboard.Models;
using BTCPayServer.Blazor.Dashboard.Widgets;
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

        // Legacy POC registration - kept for backward compatibility
        public static IServiceCollection AddWidgets(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<WidgetService>();
            serviceCollection.AddSingleton<AvailableWidget, AvailableWidget>(_ => InvoiceWidget.AvailableWidget);
            return serviceCollection;
        }

        public static IServiceCollection AddDashboardServices(this IServiceCollection services)
        {
            services.AddSingleton<WidgetRegistry>();
            services.AddScoped<DashboardService>();
            services.AddScoped<DashboardJsInterop>();
            services.AddSingleton<IDashboardTemplateProvider, DefaultStoreDashboardTemplate>();
            services.AddSingleton<IDashboardTemplateProvider, DefaultUserDashboardTemplate>();
            services.AddSingleton<IDashboardTemplateProvider, DefaultServerDashboardTemplate>();

            // Parity widgets (matching existing MVC dashboard)
            services.AddDashboardWidget<WalletBalanceWidget>(WalletBalanceWidget.Descriptor);
            services.AddDashboardWidget<StoreNumbersWidget>(StoreNumbersWidget.Descriptor);
            services.AddDashboardWidget<RecentTransactionsWidget>(RecentTransactionsWidget.Descriptor);
            services.AddDashboardWidget<RecentInvoicesWidget>(RecentInvoicesWidget.Descriptor);
            services.AddDashboardWidget<LightningBalanceWidget>(LightningBalanceWidget.Descriptor);
            services.AddDashboardWidget<LightningServicesWidget>(LightningServicesWidget.Descriptor);
            services.AddDashboardWidget<AppSalesWidget>(AppSalesWidget.Descriptor);
            services.AddDashboardWidget<AppTopItemsWidget>(AppTopItemsWidget.Descriptor);

            // Utility widgets
            services.AddDashboardWidget<NotesWidget>(NotesWidget.Descriptor);
            services.AddDashboardWidget<TodoWidget>(TodoWidget.Descriptor);
            services.AddDashboardWidget<SetupGuideWidget>(SetupGuideWidget.Descriptor);

            // Data widgets
            services.AddDashboardWidget<StatsCardWidget>(StatsCardWidget.Descriptor);
            services.AddDashboardWidget<ChartWidget>(ChartWidget.Descriptor);

            // Legacy POC support
            services.AddWidgets();

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

        /// <summary>
        /// Registers a plugin's widget contributor that appends widgets to default dashboard templates.
        /// </summary>
        public static IServiceCollection AddDashboardWidgetContributor<T>(
            this IServiceCollection services)
            where T : class, IDashboardWidgetContributor
        {
            services.AddSingleton<IDashboardWidgetContributor, T>();
            return services;
        }
    }
}
