#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Blazor.Dashboard;

public abstract class BaseWidgetComponent<TConfig> : ComponentBase where TConfig : class, new()
{
    public bool Loading
    {
        get => _loading;
        set
        {
            if (_loading == value) return;
            _loading = value;
            InvokeAsync(StateHasChanged);
        }
    }

    public virtual TConfig? TypedConfig
    {
        get => _typedConfig;
        set
        {
            _typedConfig = value;
            InvokeAsync(StateHasChanged);
            InvokeAsync(TypedConfigChanged);
        }
    }

    protected virtual Task TypedConfigChanged() => Task.CompletedTask;

    [Parameter]
    public virtual JObject? Config
    {
        get => TypedConfig is null ? null : JObject.FromObject(TypedConfig);
        set
        {
            if (Config is null && value is null)
                return;
            if (Config is not null && value is not null && JToken.DeepEquals(Config, value))
                return;
            TypedConfig = value is null ? default : value.ToObject<TConfig?>();
        }
    }

    [Parameter] public int Size { get; set; }
    [Parameter] public string StoreId { get; set; } = string.Empty;
    [Parameter] public string UserId { get; set; } = string.Empty;
    [Parameter] public bool Readonly { get; set; }

    [Parameter] public EventCallback<JObject> ConfigChanged { get; set; }
    [Parameter] public EventCallback<bool> EditModeChanged { get; set; }
    [Parameter] public EventCallback OnRemove { get; set; }

    // Access control
    protected bool HasAccess { get; set; } = true;
    protected string? AccessDeniedMessage { get; set; }

    protected TConfig? EditConfig { get; set; }

    protected bool EditMode
    {
        get => _editMode;
        set
        {
            if (_editMode == value) return;
            _editMode = value;
            InvokeAsync(() => EditModeChanged.InvokeAsync(_editMode));
            InvokeAsync(StateHasChanged);
        }
    }

    private bool _editMode;
    private bool _loading;
    private TConfig? _typedConfig;

    protected virtual Task EnterEdit()
    {
        if (EditMode)
            return Task.CompletedTask;

        EditConfig = Config is null ? new TConfig() : Config.DeepClone().ToObject<TConfig>();
        EditMode = true;
        return Task.CompletedTask;
    }

    public virtual Task CancelEdit()
    {
        EditMode = false;
        EditConfig = default;
        return Task.CompletedTask;
    }

    public virtual async Task SaveEdit()
    {
        if (EditConfig is null)
            return;

        TypedConfig = EditConfig;
        await CancelEdit();
        await ConfigChanged.InvokeAsync(Config);
    }

    public virtual async Task Remove()
    {
        await OnRemove.InvokeAsync();
    }
}
