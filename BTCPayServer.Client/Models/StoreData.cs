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

        /// <summary>
        /// the store role of the user
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// the email AND username of the user
        /// </summary>
        public string Email { get; set; }
        
        /// <summary>
        /// the name of the user
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// the image url of the user
        /// </summary>
        public string ImageUrl { get; set; }
    }

    public class RoleData
    {
        public string Id { get; set; }
        public List<string> Permissions { get; set; }
        public string Role { get; set; }
        public bool IsServerRole { get; set; }
    }
}
