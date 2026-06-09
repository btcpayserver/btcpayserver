#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BlazorDashboardKit.Abstractions;
using BlazorDashboardKit.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Plugins.Dashboard;

/// <summary>
/// Routes the kit's opaque <c>ownerKey</c> to BTCPay's existing settings stores:
/// <c>store:{storeId}</c> → per-store settings, <c>server</c> → global settings.
/// No new schema — reuses StoreRepository / SettingsRepository (PR1 dropped UserSettings).
/// </summary>
public sealed class BtcpayDashboardStore : IDashboardStore
{
    public enum OwnerScope { Store, Server }
    public const string SettingKey = "BlazorDashboard";

    private readonly StoreRepository _storeRepository;
    private readonly SettingsRepository _settingsRepository;

    public BtcpayDashboardStore(StoreRepository storeRepository, SettingsRepository settingsRepository)
    {
        _storeRepository = storeRepository;
        _settingsRepository = settingsRepository;
    }

    public static bool TryParseOwner(string? ownerKey, out OwnerScope scope, out string? storeId)
    {
        scope = OwnerScope.Server;
        storeId = null;
        if (string.IsNullOrEmpty(ownerKey))
            return false;
        if (ownerKey == "server")
        {
            scope = OwnerScope.Server;
            return true;
        }
        const string p = "store:";
        if (ownerKey.StartsWith(p, StringComparison.Ordinal) && ownerKey.Length > p.Length)
        {
            scope = OwnerScope.Store;
            storeId = ownerKey.Substring(p.Length);
            return true;
        }
        return false;
    }

    public async Task<DashboardCollection?> LoadAsync(string ownerKey, CancellationToken ct = default)
    {
        if (!TryParseOwner(ownerKey, out var scope, out var storeId))
            return null;
        return scope == OwnerScope.Server
            ? await _settingsRepository.GetSettingAsync<DashboardCollection>(SettingKey)
            : await _storeRepository.GetSettingAsync<DashboardCollection>(storeId!, SettingKey);
    }

    public async Task SaveAsync(string ownerKey, DashboardCollection collection, CancellationToken ct = default)
    {
        if (!TryParseOwner(ownerKey, out var scope, out var storeId))
            return;
        if (scope == OwnerScope.Server)
            await _settingsRepository.UpdateSetting(collection, SettingKey);
        else
            await _storeRepository.UpdateSetting(storeId!, SettingKey, collection);
    }
}
