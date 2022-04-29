using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class SettingData
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public string Value { get; set; }
        public string StoreId { get; set; }
        public StoreData Store { get; set; }

        public static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<SettingData>().HasKey(data => data.Id);
            builder.Entity<SettingData>()
                .HasIndex(x => new { DestinationId = x.Name, x.StoreId });
            builder.Entity<SettingData>()
                .HasOne(o => o.Store)
                .WithMany(o => o.Settings).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
