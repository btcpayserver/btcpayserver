#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Dashboard.Models;
using BTCPayServer.Client;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Dashboard;

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
        // For Config, compare by content. The TypedConfig setter rebuilds _cachedConfig
        // (JObject.FromObject), so reference equality would mark every parameter pass as
        // a config change and force unnecessary reload + auth checks.
        var configChanged = parameters.TryGetValue<JObject>("Config", out var newConfig)
            && !JToken.DeepEquals(newConfig, oldConfig);

        DataParametersChanged = !_hasLoadedOnce
            || oldStoreId != newStoreId
            || oldUserId != newUserId
            || oldDashboardScope != newDashboardScope
            || configChanged;

        SizeChanged = oldSize != newSize;

        // Note: _hasLoadedOnce is intentionally NOT flipped here. IsFirstLoad must
        // remain true during the first OnParametersSetAsync call (per the contract
        // documented above). The flag is set at the end of OnParametersSetAsync.
        return base.SetParametersAsync(parameters);
    }

    protected override async Task OnInitializedAsync()
    {
        if (RequiredPermissions.Length > 0)
        {
            await CheckAccess();
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        // Re-check whenever the data-relevant parameters change. The same widget
        // instance can be reused across navigation (e.g. switching stores), so a
        // cached HasAccess from the previous context would otherwise leak through.
        if (RequiredPermissions.Length > 0 && DataParametersChanged)
        {
            await CheckAccess();
        }

        // Mark the initial lifecycle pass complete only after the first
        // OnParametersSetAsync. Subclasses observing IsFirstLoad inside their own
        // OnParametersSetAsync now see true on the first call.
        _hasLoadedOnce = true;
    }

    private async Task CheckAccess()
    {
        // Re-evaluate from scratch each time so a previous denial doesn't carry over
        // when the widget context changes (e.g. switching stores) and now permits access.
        HasAccess = true;
        AccessDeniedMessage = null;

        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            foreach (var permission in RequiredPermissions)
            {
                // Store-scoped policies pass storeId as resource so
                // CookieAuthorizationHandler can resolve the store context.
                // Server-scoped policies pass null.
                object? resource = Permission.TryGetPolicyType(permission) == PolicyType.Store && !string.IsNullOrEmpty(StoreId)
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
            // Fail closed: any unexpected auth failure denies access. During prerender
            // the auth state provider may throw before the Blazor circuit is established;
            // the second render (after circuit start) re-runs CheckAccess with proper
            // auth state and can grant access if the user really is authorized.
            HasAccess = false;
            AccessDeniedMessage = "Authorization check failed.";
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

        // Persist first; only update local state and exit edit mode after the
        // callback succeeds. Otherwise a save failure leaves the UI looking saved
        // while the dashboard config never made it to storage.
        var newConfig = JObject.FromObject(EditConfig);
        await ConfigChanged.InvokeAsync(newConfig);
        TypedConfig = EditConfig;
        await CancelEdit();
    }

    public virtual async Task Remove()
    {
        await OnRemove.InvokeAsync();
    }
}
