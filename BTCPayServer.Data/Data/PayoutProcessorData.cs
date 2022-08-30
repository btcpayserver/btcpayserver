using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data.Data;

public class PayoutProcessorData
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }
    public string StoreId { get; set; }
    public StoreData Store { get; set; }
    public string PaymentMethod { get; set; }
    public string Processor { get; set; }
    
    public byte[] Blob { get; set; }
    
    internal static void OnModelCreating(ModelBuilder builder)
    {

        builder.Entity<PayoutProcessorData>()
            .HasOne(o => o.Store)
            .WithMany(data => data.PayoutProcessors).OnDelete(DeleteBehavior.Cascade);
    }

    public override string ToString()
    {
        return $"{Processor} {PaymentMethod} {StoreId}";
    }
}
