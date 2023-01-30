using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

public class StoreSettingData
{
    public string Name { get; set; }
    public string StoreId { get; set; }

    public string Value { get; set; }

    public StoreData Store { get; set; }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        builder.Entity<StoreSettingData>().HasKey(data => new { data.StoreId, data.Name });
        builder.Entity<StoreSettingData>()
            .HasOne(o => o.Store)
            .WithMany(o => o.Settings).OnDelete(DeleteBehavior.Cascade);
        if (databaseFacade.IsNpgsql())
        {
            builder.Entity<StoreSettingData>()
                .Property(o => o.Value)
                .HasColumnType("JSONB");
        }
    }
}
