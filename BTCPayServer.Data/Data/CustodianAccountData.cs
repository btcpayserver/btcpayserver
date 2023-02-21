using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

public class CustodianAccountData : IHasBlob<JObject>
{
    [Required]
    [MaxLength(50)]
    public string Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string StoreId { get; set; }

    [Required]
    [MaxLength(50)]
    public string CustodianCode { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; }

    [JsonIgnore]
    [Obsolete("Use Blob2 instead")]
    public byte[] Blob { get; set; }
    [JsonIgnore]
    public string Blob2 { get; set; }

    [JsonIgnore]
    public StoreData StoreData { get; set; }

    internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        builder.Entity<CustodianAccountData>()
            .HasOne(o => o.StoreData)
            .WithMany(i => i.CustodianAccounts)
            .HasForeignKey(i => i.StoreId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CustodianAccountData>()
            .HasIndex(o => o.StoreId);

        if (databaseFacade.IsNpgsql())
        {
            builder.Entity<CustodianAccountData>()
                .Property(o => o.Blob2)
                .HasColumnType("JSONB");
        }
    }
}
