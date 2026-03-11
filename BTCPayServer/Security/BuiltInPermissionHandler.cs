#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Security;

public class BuiltInPermissionHandler(
    StoreRepository storeRepository,
    PermissionService permissionService,
    APIKeyRepository apiKeyRepository) : IPermissionHandler
{
    public const string StoreKey = "BuiltInPermissionHandler-Store";
    public const string StoresKey = "BuiltInPermissionHandler-Stores";

    //TODO: In the future, we will add these store permissions to actual aspnet roles and remove this class.
    private static readonly PermissionSet ServerAdminRolePermissions =
        new PermissionSet(new[] { Permission.Create(Policies.CanViewStoreSettings) });

    public async Task HandleAsync(AuthorizationHandlerContext authContext, PermissionAuthorizationContext permContext)
    {
        var isAdmin = authContext.User.IsInRole(Roles.ServerAdmin);
        bool? success = null;
        StoreData? permissionedStore = null;
        List<StoreData>? permissionedStores = null;
        switch (permContext.Permission)
        {
            case { Type: PolicyType.Store }:
                if (permContext.Permission.Scope is { } storeId)
                {
                    var store = await GetStoreData(permContext, storeId, isAdmin);
                    if (store is null)
                    {
                        success = false;
                        break;
                    }
                    success =
                        (isAdmin && ServerAdminRolePermissions.HasPermission(permContext.Permission, permissionService))
                        ||
                        store.HasPolicy(permContext.UserId, permContext.Permission.Policy, permissionService);
                    if (success is true)
                        permissionedStore = store;
                }
                else
                {
                    var stores = await storeRepository.GetStoresByUserId(permContext.UserId);
                    permissionedStores = new List<StoreData>();
                    foreach (var store in stores)
                    {
                        if (authContext.HasPermission(permContext.Permission.WithScope(store.Id), permissionService) &&
                            store.HasPolicy(permContext.UserId, permContext.Permission.Policy, permissionService))
                        {
                            permissionedStores.Add(store);
                            permContext.HttpContext.AddCachedStoreData(store);
                        }
                    }
                    success = true;
                }
                break;
            case { Type: PolicyType.Server }:
                success = isAdmin;
                break;
            case { Type: PolicyType.User }:
            case { Policy: Policies.Unrestricted }:
                success = true;
                break;
        }

        if (success is true)
        {
            if (permContext.HttpContext.GetAPIKey(out var apiKey))
            {
                _ = apiKeyRepository.RecordPermissionUsage(apiKey, permContext.Permission);
            }
            authContext.Succeed(permContext.Requirement);
            if (permissionedStore is not null)
                permContext.HttpContext.Items[StoreKey] = permissionedStore;
            if (permissionedStores is not null)
                permContext.HttpContext.Items[StoresKey] = permissionedStores.ToArray();
        }
        else if (success is false)
            authContext.Fail();
    }

    private async Task<StoreData?> GetStoreData(PermissionAuthorizationContext permContext, string storeId, bool isAdmin)
    {
        var store = permContext.HttpContext.GetCachedStoreData(storeId);
        if (store is not null)
        {
            if (isAdmin ||
                store.UserStores.Any(u => u.ApplicationUserId == permContext.UserId))
                return store;
        }
        if (store is null)
            store = await storeRepository.FindStore(storeId, permContext.HttpContext.User, true);
        if (store is not null)
            permContext.HttpContext.AddCachedStoreData(store);
        return store;
    }
}
