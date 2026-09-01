using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        public string Id { get; set; }
        public string Email { get; set; }
        public string RoleId { get; set; }
    }

    public class StoreUserDataRequest
    {
        public string Id { get; set; }
        /// <summary>
        /// the store role of the user
        /// </summary>
        public string StoreRole { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();
    }
    public class AddStoreUserDataRequest : StoreUserDataRequest
    {
        public bool? RequireInvitation { get; set; }
    }

    public class AddStoreUserResult
    {
        public class InvitationResult
        {
            public string Token { get; set; }
            public string Link { get; set; }
        }
        public InvitationResult StoreInvitation {
            get;
            set;
        }
    }

    public class StoreInvitationData
    {
        public string StoreId { get; set; }
        public string StoreName { get; set; }
        public string UserId { get; set; }
        public string UserEmail { get; set; }
        public string RoleId { get; set; }
        public string InvitedByUserId { get; set; }
        public DateTimeOffset Created { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public bool IsExpired { get; set; }
        public bool IsForCurrentUser { get; set; }
    }

    public class RoleData
    {
        public string Id { get; set; }
        public List<string> Permissions { get; set; }
        public string Role { get; set; }
        public bool IsServerRole { get; set; }
    }
}
