#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Blazor;

public abstract class BaseWidgetComponent<T> : ComponentBase where T : new()
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

    public virtual T? TypedConfig
    {
        get => _typedConfig;
        set
        {
            _typedConfig = value;
            
            InvokeAsync(StateHasChanged);
            InvokeAsync(TypedConfigChanged);
        }
    }

    protected virtual  async Task TypedConfigChanged()
    {
        // await CancelEdit();
    }

    [Parameter]
    public virtual JObject? Config
    {
        get => TypedConfig is null ? null : JObject.FromObject(TypedConfig);
        set
        {
                if(Config is null && value is null)
                    return;
                if(Config is not null && value is not null && JToken.DeepEquals(Config, value))
                    return;
                TypedConfig = value is null ? default : value.ToObject<T?>();
        }
    }
    

    [Parameter] public int Size { get; set; }
    [Parameter] public string StoreId { get; set; }
    [Parameter] public bool Readonly { get; set; }

    [Parameter] public EventCallback<JObject> ConfigChanged { get; set; }
    [Parameter] public EventCallback<bool> EditModeChanged { get; set; }
    [Parameter] public EventCallback OnRemove { get; set; }

    protected T? EditConfig { get; set; }

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

    protected bool _editMode;
    private bool _loading;
    private T? _typedConfig;

    protected virtual async Task EnterEdit()
    {
        if (EditMode)
        {
            return;
        }

        EditConfig = Config is null ? new T() : Config.DeepClone().ToObject<T>();

        EditMode = true;
    }


    public virtual async Task CancelEdit()
    {
        EditMode = false;
        EditConfig = default;
    }

    public virtual async Task SaveEdit()
    {
        if (EditConfig is null)
        {
            return;
        }

        TypedConfig = EditConfig;
        await CancelEdit();
        await ConfigChanged.InvokeAsync(Config);
    }

    public virtual async Task Remove()
    {
        await OnRemove.InvokeAsync();
    }
}

public class WidgetService
{
    private readonly StoreRepository _storeRepository;
    public ImmutableArray<AvailableWidget> AvailableWidgets { get; set; }

    public WidgetService(IEnumerable<AvailableWidget> availableWidgets, StoreRepository storeRepository)
    {
        _storeRepository = storeRepository;
        AvailableWidgets = availableWidgets.ToImmutableArray();
    }

    public async Task<WidgetSet?> GetWidgetSet(string storeId)
    {
        var ws = await _storeRepository.GetSettingAsync<WidgetSet>(storeId, nameof(WidgetSet));
        return ws;
    }

    public async Task SetWidgetSet(string storeId, WidgetSet? widgetSet)
    {
        await _storeRepository.UpdateSetting(storeId, nameof(WidgetSet), widgetSet);
    }
}

public class WidgetSet
{
    public List<Widget> Widgets { get; set; }
}

public class AvailableWidget
{
    public int MinSize { get; set; }
    public int MaxSize { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public Type ComponentType { get; set; }
}

public class Widget
{
    private int _columnSize;
    private int _order;
    private int _offset;
    public string Type { get; set; }
    public JObject Config { get; set; }

    public int ColumnSize
    {
        get => _columnSize;
        set
        {
            var oldSize = _columnSize;
            _columnSize = value;
            if (_columnSize != oldSize)
                OnResize?.Invoke(this, (oldSize, _columnSize));
        }
    }

    public int Order
    {
        get => _order;
        set
        {
            var oldOrder = _order;
            _order = value;
            if (_order != oldOrder)
                OnNewOrder?.Invoke(this, (oldOrder, _order));
        }
    }
    public int Offset
    {
        get => _offset;
        set
        {
            var old = _offset;
            _offset = value;
            if (_offset != old)
                OnNewOffset?.Invoke(this, (old, _offset));
        }
    }

    [JsonIgnore] public EventHandler<(int oldValue, int newValue)> OnResize { get; set; }
    [JsonIgnore] public EventHandler<(int oldValue, int newValue)> OnNewOrder { get; set; }
    [JsonIgnore] public EventHandler<(int oldValue, int newValue)> OnNewOffset { get; set; }

    public string Id { get; set; } = Guid.NewGuid().ToString();
}
