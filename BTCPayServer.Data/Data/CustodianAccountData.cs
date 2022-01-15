using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public class CustodianAccountData
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
    
    public byte[] Blob { get; set; }
    
    public StoreData StoreData { get; set; }
    
    internal static void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<CustodianAccountData>()
            .HasOne(o => o.StoreData)
            .WithMany(i => i.CustodianAccounts)
            .HasForeignKey(i => i.StoreId).OnDelete(DeleteBehavior.Cascade);
        
        builder.Entity<APIKeyData>()
            .HasIndex(o => o.StoreId);
    }
}
