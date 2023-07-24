#nullable enable
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client;
using BTCPayServer.Data;

namespace BTCPayServer
{
    public static class StoreExtensions
    {
        public static StoreRole? GetStoreRoleOfUser(this StoreData store, string userId)
        {
            return store.UserStores.FirstOrDefault(r => r.ApplicationUserId == userId)?.StoreRole;
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
        
        public static DerivationSchemeSettings? GetDerivationSchemeSettings(this StoreData store, BTCPayNetworkProvider networkProvider, string cryptoCode)
        {
            var paymentMethod = store
                .GetSupportedPaymentMethods(networkProvider)
                .OfType<DerivationSchemeSettings>()
                .FirstOrDefault(p => p.PaymentId.PaymentType == Payments.PaymentTypes.BTCLike && p.PaymentId.CryptoCode == cryptoCode);
            return paymentMethod;
        }
        public static IEnumerable<DerivationSchemeSettings> GetDerivationSchemeSettings(this StoreData store, BTCPayNetworkProvider networkProvider)
        {
            var paymentMethod = store
                .GetSupportedPaymentMethods(networkProvider)
                .OfType<DerivationSchemeSettings>()
                .Where(p => p.PaymentId.PaymentType == Payments.PaymentTypes.BTCLike);
            return paymentMethod;
        }
    }
}
