using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser, IHasBlob<UserBlob>
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

        [Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }

        public List<IdentityUserRole<string>> UserRoles { get; set; }

        public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<ApplicationUser>()
                .HasMany<IdentityUserRole<string>>(user => user.UserRoles)
                .WithOne().HasForeignKey(role => role.UserId);
            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<ApplicationUser>()
                    .Property(o => o.Blob2)
                    .HasColumnType("JSONB");
            }
        }
    }

    public class UserBlob
    {
        public bool ShowInvoiceStatusChangeHint { get; set; }
    }
}
