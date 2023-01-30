using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class Fido2Credential : IHasBlobUntyped
    {
        public string Name { get; set; }
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }

        public string ApplicationUserId { get; set; }

        public byte[] Blob { get; set; }
        public string Blob2 { get; set; }
        public CredentialType Type { get; set; }
        public enum CredentialType
        {
            [Display(Name = "Security device (FIDO2)")]
            FIDO2,
            [Display(Name = "Lightning node (LNURL Auth)")]
            LNURLAuth
        }
        public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<Fido2Credential>()
                .HasOne(o => o.ApplicationUser)
                .WithMany(i => i.Fido2Credentials)
                .HasForeignKey(i => i.ApplicationUserId).OnDelete(DeleteBehavior.Cascade);
            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<Fido2Credential>()
                    .Property(o => o.Blob2)
                    .HasColumnType("JSONB");
            }
        }

        public ApplicationUser ApplicationUser { get; set; }
    }
}
