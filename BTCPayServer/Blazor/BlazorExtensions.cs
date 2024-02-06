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

        public static IServiceCollection AddWidgets(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<WidgetService>();
            serviceCollection.AddSingleton<AvailableWidget,AvailableWidget>(_ => InvoiceWidget.AvailableWidget);
            
            return serviceCollection;
            
        }
    }
}
