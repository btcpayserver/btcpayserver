using System.Collections.Generic;

namespace BTCPayServer.Client.Models
{
    public class StoreData : StoreBaseData
    {
        /// <summary>
        /// the id of the store
        /// </summary>
        public string Id { get; set; }
    }

    public class StoreUserData
    {
        /// <summary>
        /// the id of the user
        /// </summary>
        public string UserId { get; set; }

        public string Role { get; set; }
    }

    public class RoleData
    {
        public string Id { get; set; }
        public List<string> Permissions { get; set; }
        public string Role { get; set; }
        public bool IsServerRole { get; set; }
    }
}
