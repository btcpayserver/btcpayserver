using Microsoft.JSInterop;

namespace BTCPayServer.Blazor
{
    public static class BlazorExtensions
    {
        public static bool IsPreRendering(this IJSRuntime runtime)
            => runtime.GetType() switch
            {
                // blazor wasm (pre-rendering)
                { Name: "UnsupportedJavaScriptRuntime" } => true,
                // blazor wasm (rendering)
                { Name: "DefaultWebAssemblyJSRuntime" } => false,
                // blazor server (pre-rendering and rendering)
                { } type when type.GetProperty("IsInitialized")?.GetValue(runtime) is bool isInitialized => !isInitialized,
                _ => false
            };
    }
}
