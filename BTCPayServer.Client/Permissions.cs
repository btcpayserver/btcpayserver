using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace BTCPayServer.Client
{
    public static class Permissions
    {
        public const string ServerManagement = nameof(ServerManagement);
        public const string StoreManagement = nameof(StoreManagement);
        public const string ProfileManagement = nameof(ProfileManagement);

        public static string[] GetAllPermissionKeys()
        {
            return new[]
            {
                ServerManagement,
                StoreManagement,
                ProfileManagement
            };
        }
        public static string GetStorePermission(string storeId) => $"{nameof(StoreManagement)}:{storeId}";

        public static IEnumerable<string> ExtractStorePermissionsIds(IEnumerable<string> permissions) => permissions
            .Where(s => s.StartsWith($"{nameof(StoreManagement)}:", StringComparison.InvariantCulture))
            .Select(s => s.Split(":")[1]);
    }
}
