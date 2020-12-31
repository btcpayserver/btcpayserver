using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Data
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        public bool RequiresEmailConfirmation { get; set; }
        public List<StoredFile> StoredFiles { get; set; }
        public List<U2FDevice> U2FDevices { get; set; }
        public List<APIKeyData> APIKeys { get; set; }
        public DateTimeOffset? Created { get; set; }
        public string DisabledNotifications { get; set; }

        public List<NotificationData> Notifications { get; set; }
        public List<UserStore> UserStores { get; set; }
    }
}
