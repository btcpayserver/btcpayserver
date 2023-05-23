using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

public class LightningAddressData : IHasBlob<LightningAddressDataBlob>
{
    public string Username { get; set; }
    public string StoreDataId { get; set; }
    [Obsolete("Use Blob2 instead")]
    public byte[] Blob { get; set; }
    public string Blob2 { get; set; }

    public StoreData Store { get; set; }


    internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        builder.Entity<LightningAddressData>()
            .HasOne(o => o.Store)
            .WithMany(a => a.LightningAddresses)
            .HasForeignKey(data => data.StoreDataId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<LightningAddressData>().HasKey(o => o.Username);
        if (databaseFacade.IsNpgsql())
        {
            builder.Entity<LightningAddressData>()
                .Property(o => o.Blob2)
                .HasColumnType("JSONB");
        }
    }
}

public class LightningAddressDataBlob
{
    public string CurrencyCode { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }

    public JObject InvoiceMetadata { get; set; }
    
    [JsonExtensionData] public Dictionary<string, JToken> AdditionalData { get; set; }
    
}
