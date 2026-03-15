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

    // --- Gridstack integration ---

    public async Task InitGrid(ElementReference container, DotNetObjectReference<object> dotNetHelper, bool editMode)
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("DashboardInterop.initGrid", container, dotNetHelper, editMode);
    }

    public async Task SetEditMode(bool editMode)
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("DashboardInterop.setEditMode", editMode);
    }

    public async Task DestroyGrid()
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("DashboardInterop.destroyGrid");
    }

    public async Task AddGridWidget(ElementReference element)
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("DashboardInterop.addGridWidget", element);
    }

    public async Task RemoveGridWidget(ElementReference element)
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("DashboardInterop.removeGridWidget", element);
    }

    // --- Charts ---

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

    // --- Utilities ---

    public async Task CopyToClipboard(string text)
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
    }

    public async Task DownloadJson(string filename, string json)
    {
        if (_jsRuntime.IsPreRendering())
            return;
        await _jsRuntime.InvokeVoidAsync("DashboardInterop.downloadJson", filename, json);
    }

    public async Task<string?> ReadFileAsText(ElementReference input)
    {
        if (_jsRuntime.IsPreRendering())
            return null;
        return await _jsRuntime.InvokeAsync<string?>("DashboardInterop.readFileAsText", input);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
