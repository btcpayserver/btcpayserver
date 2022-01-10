using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class U2FDevice
    {
        public string Id { get; set; }

        public string Name { get; set; }

        [Required] public byte[] KeyHandle { get; set; }

        [Required] public byte[] PublicKey { get; set; }

        [Required] public byte[] AttestationCert { get; set; }

        [Required] public int Counter { get; set; }

        public string ApplicationUserId { get; set; }
        public ApplicationUser ApplicationUser { get; set; }


        internal static void OnModelCreating(ModelBuilder builder)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            builder.Entity<U2FDevice>()
                .HasOne(o => o.ApplicationUser)
                .WithMany(i => i.U2FDevices)
                .HasForeignKey(i => i.ApplicationUserId).OnDelete(DeleteBehavior.Cascade);
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
