using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

public class AutomatedPayoutBlob
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
}
public class PayoutProcessorData : IHasBlobUntyped
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }
    public string StoreId { get; set; }
    public StoreData Store { get; set; }
    public string PaymentMethod { get; set; }
    public string Processor { get; set; }

    [Obsolete("Use Blob2 instead")]
    public byte[] Blob { get; set; }
    public string Blob2 { get; set; }

    internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        builder.Entity<PayoutProcessorData>()
            .HasOne(o => o.Store)
            .WithMany(data => data.PayoutProcessors).OnDelete(DeleteBehavior.Cascade);

        if (databaseFacade.IsNpgsql())
        {
            builder.Entity<PayoutProcessorData>()
                .Property(o => o.Blob2)
                .HasColumnType("JSONB");
        }
    }

    public override string ToString()
    {
        return $"{Processor} {PaymentMethod} {StoreId}";
    }
}
