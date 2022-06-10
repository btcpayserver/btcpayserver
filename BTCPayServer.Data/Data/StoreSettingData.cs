using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public class StoreSettingData
{
    public string Name { get; set; }
    public string StoreId { get; set; }

    public string Value { get; set; }

    public StoreData Store { get; set; }
        
    public static void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<StoreSettingData>().HasKey(data => new { data.Name, data.StoreId});
        builder.Entity<StoreSettingData>()
            .HasOne(o => o.Store)
            .WithMany(o => o.Settings).OnDelete(DeleteBehavior.Cascade);
    }
}
