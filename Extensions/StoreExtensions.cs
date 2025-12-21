#nullable enable
using System.Linq;
using BTCPayServer.Client;
using BTCPayServer.Data;
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

        public static bool HasPermission(this StoreData store, string userId, string permission)
        {
            return GetPermissionSet(store, userId).HasPermission(permission, store.Id);
        }

        public static bool HasPermission(this PermissionSet permissionSet, string permission, string storeId)
        {
            return permissionSet.Contains(permission, storeId);
        }
        
        public static DerivationSchemeSettings? GetDerivationSchemeSettings(this StoreData store, PaymentMethodHandlerDictionary handlers, string cryptoCode, bool onlyEnabled = false)
        {
            var pmi = Payments.PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            return store.GetPaymentMethodConfig<DerivationSchemeSettings>(pmi, handlers, onlyEnabled);
        }
    }
}
