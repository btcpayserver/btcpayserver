using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class Fido2Credential
    {
        public string Name { get; set; }
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }
        public byte[] DescriptorBlob { get; set; }
        public byte[] PublicKey { get; set; }
        public byte[] UserHandle { get; set; }
        public uint SignatureCounter { get; set; }
        public string CredType { get; set; }
        public DateTimeOffset RegDate { get; set; }
        public string AaGuid { get; set; }
        
        
        public static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Fido2Credential>()
                .HasOne(o => o.ApplicationUser)
                .WithMany(i => i.Fido2Credentials)
                .HasForeignKey(i => i.ApplicationUserId).OnDelete(DeleteBehavior.Cascade);
            
        }

        public ApplicationUser ApplicationUser { get; set; }

        public string ApplicationUserId { get; set; }
    }
}
