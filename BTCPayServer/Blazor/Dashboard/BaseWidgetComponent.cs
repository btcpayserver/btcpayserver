#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Blazor.Dashboard.Models;
using BTCPayServer.Client;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Blazor.Dashboard;

public abstract class BaseWidgetComponent<TConfig> : ComponentBase where TConfig : class, new()
{
    [Inject] private IAuthorizationService AuthorizationService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = null!;

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
            _cachedConfig = value is null ? null : JObject.FromObject(value);
            InvokeAsync(StateHasChanged);
            InvokeAsync(TypedConfigChanged);
        }
    }

    protected virtual Task TypedConfigChanged() => Task.CompletedTask;

    [Parameter]
    public virtual JObject? Config
    {
        get => _cachedConfig;
        set
        {
            if (_cachedConfig is null && value is null)
                return;
            if (_cachedConfig is not null && value is not null && JToken.DeepEquals(_cachedConfig, value))
                return;
            TypedConfig = value is null ? default : value.ToObject<TConfig?>();
        }
    }

    [Parameter] public int Size { get; set; }
    [Parameter] public string StoreId { get; set; } = string.Empty;
    [Parameter] public string UserId { get; set; } = string.Empty;
    [Parameter] public bool Readonly { get; set; }
    [Parameter] public string[] RequiredPermissions { get; set; } = Array.Empty<string>();
    [Parameter] public DashboardScope DashboardScope { get; set; } = DashboardScope.Store;

    [Parameter] public EventCallback<JObject> ConfigChanged { get; set; }
    [Parameter] public EventCallback<bool> EditModeChanged { get; set; }
    [Parameter] public EventCallback OnRemove { get; set; }
    [Parameter] public EventCallback RequestConfigure { get; set; }

    // Access control
    protected bool HasAccess { get; set; } = true;
    protected string? AccessDeniedMessage { get; set; }

    protected TConfig? EditConfig { get; set; }

    /// <summary>
    /// True when data-relevant parameters (StoreId, Config) have changed since the last render,
    /// meaning the widget should re-fetch its data. Widgets should check this at the top of
    /// OnParametersSetAsync and skip expensive data loading when false.
    /// </summary>
    protected bool DataParametersChanged { get; private set; }

    /// <summary>
    /// True when the Size parameter changed, meaning chart widgets should re-render their charts.
    /// </summary>
    protected bool SizeChanged { get; private set; }

    /// <summary>
    /// True only on the very first OnParametersSetAsync call (initial load).
    /// </summary>
    protected bool IsFirstLoad => !_hasLoadedOnce;

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
    private JObject? _cachedConfig;
    private bool _hasLoadedOnce;

    public override Task SetParametersAsync(ParameterView parameters)
    {
        // Snapshot previous values before Blazor applies new ones
        var oldStoreId = StoreId;
        var oldUserId = UserId;
        var oldConfig = _cachedConfig;
        var oldSize = Size;
        var oldDashboardScope = DashboardScope;

        // Determine if data-relevant parameters changed by peeking at incoming values.
        // We check before base.SetParametersAsync because OnParametersSetAsync runs inside it.
        var newStoreId = parameters.TryGetValue<string>("StoreId", out var s) ? s : oldStoreId;
        var newUserId = parameters.TryGetValue<string>("UserId", out var u) ? u : oldUserId;
        var newSize = parameters.TryGetValue<int>("Size", out var sz) ? sz : oldSize;
        var newDashboardScope = parameters.TryGetValue<DashboardScope>("DashboardScope", out var ds) ? ds : oldDashboardScope;
        // For Config (JObject), any new instance means potential change
        var configChanged = parameters.TryGetValue<JObject>("Config", out var newConfig)
            && !ReferenceEquals(newConfig, oldConfig);

        DataParametersChanged = !_hasLoadedOnce
            || oldStoreId != newStoreId
            || oldUserId != newUserId
            || oldDashboardScope != newDashboardScope
            || configChanged;

        SizeChanged = oldSize != newSize;

        if (DataParametersChanged)
            _hasLoadedOnce = true;

        // Now let Blazor apply parameters and run OnParametersSetAsync
        return base.SetParametersAsync(parameters);
    }

    protected override async Task OnInitializedAsync()
    {
        if (RequiredPermissions.Length > 0)
        {
            await CheckAccess();
        }
    }

    private async Task CheckAccess()
    {
        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            foreach (var permission in RequiredPermissions)
            {
                // Store-scoped policies pass storeId as resource so
                // CookieAuthorizationHandler can resolve the store context.
                // Server-scoped policies pass null.
                object? resource = Policies.IsStorePolicy(permission) && !string.IsNullOrEmpty(StoreId)
                    ? StoreId
                    : null;

                var result = await AuthorizationService.AuthorizeAsync(
                    user, resource, new PolicyRequirement(permission));

                if (!result.Succeeded)
                {
                    HasAccess = false;
                    AccessDeniedMessage = "You do not have permission to view this widget.";
                    return;
                }
            }
        }
        catch
        {
            // If authorization check fails (e.g. during prerender), allow access
            HasAccess = true;
        }
    }

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
