#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BTCPayServer.Blazor.Dashboard;

public class DashboardJsInterop : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;

    public DashboardJsInterop(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task InitSortable(ElementReference container, DotNetObjectReference<object> dotNetHelper)
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("DashboardInterop.initSortable", container, dotNetHelper);
    }

    public async Task DestroySortable()
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("DashboardInterop.destroySortable");
    }

    public async Task RenderChart(string elementId, string type, object labels, object series, object? options = null)
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("DashboardInterop.renderChart", elementId, type, labels, series, options);
    }

    public async Task DestroyChart(string elementId)
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("DashboardInterop.destroyChart", elementId);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
