using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace BTCPayServer.Security.APIKeys
{
    public static class APIKeyConstants
    {
        public const string AuthenticationType = "APIKey";

        public static class ClaimTypes
        {
            public const string Permissions = nameof(APIKeys) + "." + nameof(Permissions);
        }

        public static class Permissions
        {
            public const string ServerManagement = nameof(ServerManagement);
            public const string StoreManagement = nameof(StoreManagement);

            public static readonly Dictionary<string, (string Title, string Description)> PermissionDescriptions = new Dictionary<string, (string Title, string Description)>()
            {
                {StoreManagement, ("Manage your stores", "The app will be able to create, modify and delete all your stores.")},
                {$"{nameof(StoreManagement)}:", ("Manage selected stores", "The app will be able to modify and delete selected stores.")},
                {ServerManagement, ("Manage your server", "The app will have total control on your server")},
            };

            public static string GetStorePermission(string storeId) => $"{nameof(StoreManagement)}:{storeId}";

            public static IEnumerable<string> ExtractStorePermissionsIds(IEnumerable<string> permissions) => permissions
                .Where(s => s.StartsWith($"{nameof(StoreManagement)}:", StringComparison.InvariantCulture))
                .Select(s => s.Split(":")[1]);
        }
    }
}
