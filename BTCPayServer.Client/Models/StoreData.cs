using System;
using System.Collections.Generic;
using Newtonsoft.Json;

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

        /// <summary>
        /// the invitation url of the user
        /// </summary>
        public string InvitationUrl { get; set; }

        /// <summary>
        /// the invitation token of the user
        /// </summary>
        public string InvitationToken { get; set; }

        /// <summary>
        /// Whether the user has verified their email
        /// </summary>
        public bool EmailConfirmed { get; set; }

        /// <summary>
        /// whether the user needed to verify their email on account creation
        /// </summary>
        public bool RequiresEmailConfirmation { get; set; }

        /// <summary>
        /// Whether the user was approved by an admin
        /// </summary>
        public bool Approved { get; set; }

        /// <summary>
        /// whether the user needed approval on account creation
        /// </summary>
        public bool RequiresApproval { get; set; }

        /// <summary>
        /// the date the user was created. Null if created before v1.0.5.6.
        /// </summary>
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? Created { get; set; }

        public bool Disabled { get; set; }
    }

    public class RoleData
    {
        public string Id { get; set; }
        public List<string> Permissions { get; set; }
        public string Role { get; set; }
        public bool IsServerRole { get; set; }
    }
}
