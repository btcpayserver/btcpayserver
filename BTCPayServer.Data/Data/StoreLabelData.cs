#nullable enable
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public class StoreLabelData
{
    public string StoreId { get; set; } = null!;
    public string LabelId { get; set; } = null!;

    public string? Data { get; set; }

    [Timestamp]
    public uint XMin { get; set; }

    internal static void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<StoreLabelData>()
            .HasKey(o => new { o.StoreId, o.LabelId });

        builder.Entity<StoreLabelData>()
            .Property(o => o.Data)
            .HasColumnType("JSONB");
    }
}
