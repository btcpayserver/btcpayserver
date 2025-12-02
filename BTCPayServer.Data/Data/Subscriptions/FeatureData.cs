#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subs_features")]
public class FeatureData
{
    /// <summary>
    /// The internal ID of the feature, we only really use it in
    /// SQL queries. This should not be exposed.
    /// </summary>
    [Required]
    [Column("id")]
    [Key]
    public long Id { get; set; }

    /// <summary>
    /// The ID selected by the user, scoped at the offering level.
    /// </summary>
    [Required]
    [Column("custom_id")]
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
        var b = builder.Entity<FeatureData>();
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityAlwaysColumn();
        b.HasIndex(x => new { x.OfferingId, x.CustomId }).IsUnique();
        b.HasOne(x => x.Offering).WithMany(x => x.Features).HasForeignKey(x => x.OfferingId).OnDelete(DeleteBehavior.Cascade);
    }
}
