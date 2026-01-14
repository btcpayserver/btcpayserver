#nullable enable
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public class StoreLabelLinkData
{
    public string StoreId { get; set; } = null!;
    public string LabelId { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string ObjectId { get; set; } = null!;

    public string? Data { get; set; }

    [Timestamp]
    public uint XMin { get; set; }

    public static void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<StoreLabelLinkData>()
            .HasKey(o => new { o.StoreId, o.LabelId, o.Type, o.ObjectId });

        builder.Entity<StoreLabelLinkData>()
            .Property(o => o.Data)
            .HasColumnType("JSONB");

        builder.Entity<StoreLabelLinkData>()
            .HasIndex(o => new { o.StoreId, o.Type, o.ObjectId });

        builder.Entity<StoreLabelLinkData>()
            .HasIndex(o => new { o.StoreId, o.Type, o.LabelId });

        builder.Entity<StoreLabelLinkData>()
            .HasOne<StoreLabelData>()
            .WithMany()
            .HasForeignKey(o => new { o.StoreId, o.LabelId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
