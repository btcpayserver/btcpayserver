#nullable enable
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public class StoreLabelData
{
    public string StoreId { get; set; } = null!;
    public string Id { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Text { get; set; } = null!;
    public string? Color { get; set; }

    [Timestamp]
    public uint XMin { get; set; }

    internal static void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<StoreLabelData>(b =>
        {
            b.ToTable("store_labels");

            b.HasKey(x => new { x.StoreId, x.Id });

            b.Property(x => x.StoreId).HasColumnName("store_id");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Type).HasColumnName("type");
            b.Property(x => x.Text).HasColumnName("text");
            b.Property(x => x.Color).HasColumnName("color");
            b.Property(x => x.XMin).HasColumnName("xmin");

        });
    }
}
