#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Blazor.Dashboard.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Blazor.Dashboard;

public class DashboardService
{
    private readonly StoreRepository _storeRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly UserSettingsRepository _userSettingsRepository;
    private readonly IEnumerable<IDashboardTemplateProvider> _templateProviders;
    private readonly IEnumerable<IDashboardWidgetContributor> _widgetContributors;

    private const string SettingName = "DashboardCollection";

    public DashboardService(
        StoreRepository storeRepository,
        SettingsRepository settingsRepository,
        UserSettingsRepository userSettingsRepository,
        IEnumerable<IDashboardTemplateProvider> templateProviders,
        IEnumerable<IDashboardWidgetContributor> widgetContributors)
    {
        _storeRepository = storeRepository;
        _settingsRepository = settingsRepository;
        _userSettingsRepository = userSettingsRepository;
        _templateProviders = templateProviders;
        _widgetContributors = widgetContributors;
    }

    // --- Server-level ---
    public async Task<DashboardCollection> GetServerDashboards()
    {
        return await _settingsRepository.GetSettingAsync<DashboardCollection>(SettingName)
               ?? new DashboardCollection();
    }

    public async Task SaveServerDashboards(DashboardCollection collection)
    {
        await _settingsRepository.UpdateSetting(collection, SettingName);
    }

    // --- Store-level ---
    public async Task<DashboardCollection> GetStoreDashboards(string storeId)
    {
        return await _storeRepository.GetSettingAsync<DashboardCollection>(storeId, SettingName)
               ?? new DashboardCollection();
    }

    public async Task SaveStoreDashboards(string storeId, DashboardCollection collection)
    {
        await _storeRepository.UpdateSetting(storeId, SettingName, collection);
    }

    // --- User-level ---
    public async Task<DashboardCollection> GetUserDashboards(string userId)
    {
        return await _userSettingsRepository.GetSettingAsync<DashboardCollection>(userId, SettingName)
               ?? new DashboardCollection();
    }

    public async Task SaveUserDashboards(string userId, DashboardCollection collection)
    {
        await _userSettingsRepository.UpdateSetting(userId, SettingName, collection);
    }

    // --- Resolution ---
    public async Task<DashboardDefinition> ResolveActiveDashboard(
        string? userId, string? storeId, DashboardTemplateContext context)
    {
        // Priority: user > store > server > code default
        if (userId is not null)
        {
            var userCollection = await GetUserDashboards(userId);
            var active = FindActive(userCollection, storeId);
            if (active is not null)
                return active;
        }

        if (storeId is not null)
        {
            var storeCollection = await GetStoreDashboards(storeId);
            var active = FindActive(storeCollection);
            if (active is not null)
                return active;
        }

        var serverCollection = await GetServerDashboards();
        var serverActive = FindActive(serverCollection);
        if (serverActive is not null)
            return serverActive;

        // Fall back to code-defined default
        return GetDefaultTemplate(DashboardScope.Store, context);
    }

    // --- Copy ---
    public static DashboardDefinition CopyDashboard(DashboardDefinition source, string newName)
    {
        var copy = new DashboardDefinition
        {
            Id = Guid.NewGuid().ToString(),
            Name = newName,
            Description = source.Description,
            Scope = source.Scope,
            IsDefault = false,
            SourceTemplateId = source.Id,
            Widgets = source.Widgets.Select(w => new WidgetPlacement
            {
                Id = Guid.NewGuid().ToString(),
                WidgetType = w.WidgetType,
                Order = w.Order,
                ColumnSize = w.ColumnSize,
                Offset = w.Offset,
                Config = w.Config?.DeepClone() as JObject
            }).ToList()
        };
        return copy;
    }

    // --- Templates ---
    public DashboardDefinition GetDefaultTemplate(DashboardScope scope, DashboardTemplateContext context)
    {
        var provider = _templateProviders.FirstOrDefault(p => p.Scope == scope);
        var template = provider?.GetTemplate(context) ?? new DashboardDefinition
        {
            Name = "Empty Dashboard",
            Scope = scope,
            IsDefault = true
        };

        // Append plugin-contributed widgets
        var nextOrder = template.Widgets.Count > 0
            ? template.Widgets.Max(w => w.Order) + 1
            : 0;

        foreach (var contributor in _widgetContributors)
        {
            if (!contributor.ApplicableScopes.Contains(scope))
                continue;

            foreach (var placement in contributor.GetWidgets(scope, context))
            {
                placement.Order = nextOrder++;
                if (string.IsNullOrEmpty(placement.Id))
                    placement.Id = Guid.NewGuid().ToString();
                template.Widgets.Add(placement);
            }
        }

        return template;
    }

    private static DashboardDefinition? FindActive(DashboardCollection collection, string? storeId = null)
    {
        if (collection.Dashboards.Count == 0)
            return null;

        if (collection.ActiveDashboardId is not null)
        {
            var active = collection.Dashboards.FirstOrDefault(d => d.Id == collection.ActiveDashboardId);
            if (active is not null)
                return active;
        }

        return collection.Dashboards.FirstOrDefault(d => d.IsDefault)
               ?? collection.Dashboards.FirstOrDefault();
    }
}
