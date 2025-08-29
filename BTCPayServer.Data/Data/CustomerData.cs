#nullable  enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Data;

[Table("customers")]
public class CustomerData
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = null!;

    [Required]
    [Column("store_id")]
    public string StoreId { get; set; } = null!;

    [ForeignKey("StoreId")]
    public StoreData Store { get; set; } = null!;

    // Identity
    [Column("external_ref")]
    public string? ExternalRef { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    // Operational
    [Column("created_at", TypeName = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Column("metadata", TypeName = "jsonb")]
    public string Metadata { get; set; } = "{}";


    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<CustomerData>();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz")
            .HasDefaultValueSql("now()");
        b.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        b.HasKey(x => new { x.Id });
        b.HasIndex(x => new { x.StoreId, x.Email }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.ExternalRef }).IsUnique();
    }

    public static string GenerateId() => Encoders.Base58.EncodeData(RandomUtils.GetBytes(13));
}
