#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BlazorDashboardKit.Abstractions;
using BlazorDashboardKit.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;

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
    private readonly BtcpayDashboardContext _context;
    private readonly ILogger<BtcpayDashboardStore> _logger;

    public BtcpayDashboardStore(StoreRepository storeRepository, SettingsRepository settingsRepository, BtcpayDashboardContext context, ILogger<BtcpayDashboardStore> logger)
    {
        _storeRepository = storeRepository;
        _settingsRepository = settingsRepository;
        _context = context;
        _logger = logger;
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
        // Owner-scoped write guard: the only writable target is the page scope this
        // circuit was rendered for. The JS-reachable grid callback funnels through here.
        if (scope == OwnerScope.Server ? _context.OwnerKey != ownerKey : !string.Equals(storeId, _context.StoreId, StringComparison.Ordinal) || string.IsNullOrEmpty(storeId))
        {
            _logger.LogWarning("Rejected dashboard save for {OwnerKey}: outside the circuit page scope", ownerKey);
            return;
        }
        if (scope == OwnerScope.Server)
            await _settingsRepository.UpdateSetting(collection, SettingKey);
        else
            await _storeRepository.UpdateSetting(storeId!, SettingKey, collection);
    }
}
