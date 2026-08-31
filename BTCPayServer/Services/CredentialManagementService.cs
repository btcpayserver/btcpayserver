#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Services;

public class CredentialManagementService(
    StoreRepository storeRepository,
    PermissionService permissionService,
    ISettingsAccessor<PoliciesSettings> policiesSettings)
{
    public bool IsAllowedByServer(ClaimsPrincipal user)
    {
        return user.IsInRole(Roles.ServerAdmin) ||
               !policiesSettings.Settings.DisableNonAdminCredentialManagement;
    }

    public async Task<StoreData[]> GetManageableStores(ClaimsPrincipal user)
    {
        var userId = user.GetIdOrNull();
        if (userId is null || !IsAllowedByServer(user))
            return [];

        var stores = await storeRepository.GetStoresByUserId(userId);
        if (user.IsInRole(Roles.ServerAdmin))
            return stores;

        return stores
            .Where(store => store.HasPolicy(userId, Policies.CanManageStoreCredentials, permissionService))
            .ToArray();
    }

    public async Task<bool> CanManageAccountApiKeys(ClaimsPrincipal user)
    {
        var userId = user.GetIdOrNull();
        if (userId is null || !IsAllowedByServer(user))
            return false;
        if (user.IsInRole(Roles.ServerAdmin))
            return true;

        var stores = await storeRepository.GetStoresByUserId(userId);
        return stores.Length is 0 || stores.Any(store =>
            store.HasPolicy(userId, Policies.CanManageStoreCredentials, permissionService));
    }

    public async Task<bool> CanCreateApiKey(ClaimsPrincipal user, IEnumerable<Permission> requestedPermissions)
    {
        if (!await CanManageAccountApiKeys(user))
            return false;
        if (user.IsInRole(Roles.ServerAdmin))
            return true;

        var userId = user.GetIdOrNull();
        if (userId is null)
            return false;

        var stores = await storeRepository.GetStoresByUserId(userId);
        var manageableStoreIds = stores
            .Where(store => store.HasPolicy(userId, Policies.CanManageStoreCredentials, permissionService))
            .Select(store => store.Id)
            .ToHashSet();

        foreach (var permission in requestedPermissions)
        {
            if (permission.Type is PolicyType.Server)
                return false;

            if (permission.Type is not PolicyType.Store && permission.Policy != Policies.Unrestricted)
                continue;

            if (permission.Scope is { } storeId)
            {
                if (!manageableStoreIds.Contains(storeId))
                    return false;
            }
            else if (stores.Any(store => !manageableStoreIds.Contains(store.Id)))
            {
                return false;
            }
        }

        return true;
    }
}
