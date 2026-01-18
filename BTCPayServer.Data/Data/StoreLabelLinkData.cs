#nullable enable
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public class StoreLabelLinkData
{
    public string StoreId { get; set; } = null!;
    public string StoreLabelId { get; set; } = null!;
    public string ObjectId { get; set; } = null!;

    public StoreLabelData StoreLabel { get; set; } = null!;

    [Timestamp]
    public uint XMin { get; set; }

    public static void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<StoreLabelLinkData>(b =>
        {
            b.ToTable("store_label_links");

            b.HasKey(x => new { x.StoreId, x.StoreLabelId, x.ObjectId });

            b.Property(x => x.StoreId).HasColumnName("store_id");
            b.Property(x => x.StoreLabelId).HasColumnName("store_label_id");
            b.Property(x => x.ObjectId).HasColumnName("object_id");
            b.Property(x => x.XMin).HasColumnName("xmin");

            b.HasIndex(x => new { x.StoreId, x.ObjectId });

            b.HasOne(x => x.StoreLabel)
                .WithMany()
                .HasForeignKey(x => new { x.StoreId, x.StoreLabelId })
                .HasPrincipalKey(x => new { x.StoreId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
