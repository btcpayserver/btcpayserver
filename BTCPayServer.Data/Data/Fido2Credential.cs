using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class Fido2Credential
    {
        public string Name { get; set; }
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }

        public string ApplicationUserId { get; set; }

        public byte[] Blob { get; set; }
        public CredentialType Type { get; set; }
        public enum CredentialType
        {
            [Display(Name = "Security device (FIDO2)")]
            FIDO2,
            [Display(Name = "Lightning node (LNURL Auth)")]
            LNURLAuth
        }
        public static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Fido2Credential>()
                .HasOne(o => o.ApplicationUser)
                .WithMany(i => i.Fido2Credentials)
                .HasForeignKey(i => i.ApplicationUserId).OnDelete(DeleteBehavior.Cascade);
        }

        public ApplicationUser ApplicationUser { get; set; }
    }
}
