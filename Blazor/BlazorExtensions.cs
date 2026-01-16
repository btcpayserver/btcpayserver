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
    }
}
