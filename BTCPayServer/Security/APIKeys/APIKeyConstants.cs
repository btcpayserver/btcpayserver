using System.Collections.Generic;

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
            public static readonly Dictionary<string, (string Title, string Description)> PermissionDescriptions = new Dictionary<string, (string Title, string Description)>()
            {
                {Client.Permissions.StoreManagement, ("Manage your stores", "The app will be able to create, modify and delete all your stores.")},
                {$"{nameof(Client.Permissions.StoreManagement)}:", ("Manage selected stores", "The app will be able to modify and delete selected stores.")},
                {Client.Permissions.ServerManagement, ("Manage your server", "The app will have total control on your server")},
                {Client.Permissions.ProfileManagement, ("Manage your profile", "The app will be able to view and modify your user profile.")},
            };

        }
    }
}
