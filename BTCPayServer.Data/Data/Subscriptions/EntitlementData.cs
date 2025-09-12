#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subscriptions_entitlements")]
public class EntitlementData
{
    [Key]
    [Required]
    [Column("name")]
    public string Name { get; set; } = null!;
    [Required]
    [Column("id")]
    [Key]
    public long Id { get; set; }
    [Required]
    public string CustomId { get; set; } = null!;
    [Required]
    [Column("offering_id")]
    public string OfferingId { get; set; } = null!;

    [ForeignKey(nameof(OfferingId))]
    public OfferingData Offering { get; set; } = null!;

    [Column("description")]
    public string? Description { get; set; }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<EntitlementData>();
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.HasIndex(x => new { x.OfferingId, x.CustomId }).IsUnique();
        b.HasOne(x => x.Offering).WithMany(x => x.Entitlements).HasForeignKey(x => x.OfferingId).OnDelete(DeleteBehavior.Cascade);
    }
}
