using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public class LightningAddressData
{
    public string Username { get; set; }
    public string StoreDataId { get; set; }
    public byte[] Blob { get; set; }

    public StoreData Store { get; set; }


    internal static void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<LightningAddressData>()
            .HasOne(o => o.Store)
            .WithMany(a => a.LightningAddresses)
            .HasForeignKey(data => data.StoreDataId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<LightningAddressData>().HasKey(o => o.Username);
    }
}

public class LightningAddressDataBlob
{
    public string CurrencyCode { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
}
