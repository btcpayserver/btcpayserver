#nullable enable
using System.Linq;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer
{
    public static class StoreExtensions
    {
        public static StoreRole? GetStoreRoleOfUser(this StoreData store, string userId)
        {
            return store.UserStores?.FirstOrDefault(r => r.ApplicationUserId == userId)?.StoreRole;
        }

        public static PermissionSet GetPermissionSet(this StoreRole storeRole, string storeId)
        {
            return new PermissionSet(storeRole.Permissions
                .Select(s => Permission.TryCreatePermission(s, storeId, out var permission) ? permission : null)
                .Where(s => s != null).ToArray());
        }


        public static PermissionSet GetPermissionSet(this StoreData store, string userId)
        {
           return  store.GetStoreRoleOfUser(userId)?.GetPermissionSet(store.Id)?? new PermissionSet();
        }

        public static bool HasPolicy(this StoreData store, string userId, string policy, PermissionService permissionService)
        {
            if (!Permission.TryCreatePermission(policy, store.Id, out var requiredPermission))
                return false;
            return GetPermissionSet(store, userId).HasPermission(requiredPermission, permissionService);
        }

        public static bool HasPermission(this PermissionSet permissionSet, Permission permission, PermissionService permissionService)
        {
            foreach (var existing in permissionSet.Permissions)
            {
                if (permissionService.Contains(existing, permission))
                    return true;
            }
            return false;
        }

        public static DerivationSchemeSettings? GetDerivationSchemeSettings(this StoreData store, PaymentMethodHandlerDictionary handlers, string cryptoCode, bool onlyEnabled = false)
        {
            var pmi = Payments.PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            return store.GetPaymentMethodConfig<DerivationSchemeSettings>(pmi, handlers, onlyEnabled);
        }
    }
}
