using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class SettingData
    {
        public string Id { get; set; }

        public string Value { get; set; }

        public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<SettingData>()
                .Property(o => o.Value)
                .HasColumnType("JSONB");
        }
    }
}
