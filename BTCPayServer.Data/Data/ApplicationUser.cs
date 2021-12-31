using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        public bool RequiresEmailConfirmation { get; set; }
        public List<StoredFile> StoredFiles { get; set; }
        [Obsolete("U2F support has been replace with FIDO2")]
        public List<U2FDevice> U2FDevices { get; set; }
        public List<APIKeyData> APIKeys { get; set; }
        public DateTimeOffset? Created { get; set; }
        public string DisabledNotifications { get; set; }

        public List<NotificationData> Notifications { get; set; }
        public List<UserStore> UserStores { get; set; }
        public List<Fido2Credential> Fido2Credentials { get; set; }

        public byte[] Blob { get; set; }

        public List<IdentityUserRole<string>> UserRoles { get; set; }

        public static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<ApplicationUser>()
                .HasMany<IdentityUserRole<string>>(user => user.UserRoles)
                .WithOne().HasForeignKey(role => role.UserId);
        }
    }
}
