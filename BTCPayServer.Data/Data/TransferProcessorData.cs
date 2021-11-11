using System.ComponentModel.DataAnnotations.Schema;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.TransferProcessors;

public class TransferProcessorData
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

        builder.Entity<TransferProcessorData>()
            .HasOne(o => o.Store)
            .WithMany(data => data.TransferProcessors).OnDelete(DeleteBehavior.Cascade);
    }
}
