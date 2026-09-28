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

/// <summary>
/// Applies the server policy and store-role boundary for API key and access-token management.
/// </summary>
public class CredentialManagementService(
    StoreRepository storeRepository,
    PermissionService permissionService,
    ISettingsAccessor<PoliciesSettings> policiesSettings)
{
    /// <summary>
    /// Determines whether the server-wide policy permits the user to manage credentials.
    /// </summary>
    public bool IsAllowedByServer(ClaimsPrincipal user)
    {
        return user.IsInRole(Roles.ServerAdmin) ||
               !policiesSettings.Settings.DisableNonAdminCredentialManagement;
    }

    /// <summary>
    /// Returns stores for which the user may manage credentials under both server and store policy.
    /// </summary>
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

    /// <summary>
    /// Determines whether the user may access account-level API key management.
    /// Store roles do not apply here: they only constrain the store scopes a key may carry.
    /// </summary>
    public bool CanManageAccountApiKeys(ClaimsPrincipal user)
    {
        return user.GetIdOrNull() is not null && IsAllowedByServer(user);
    }

    /// <summary>
    /// Determines whether the user may create an API key with the requested permissions and store scopes.
    /// </summary>
    public async Task<bool> CanCreateApiKey(ClaimsPrincipal user, IEnumerable<Permission> requestedPermissions)
    {
        if (!CanManageAccountApiKeys(user))
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
