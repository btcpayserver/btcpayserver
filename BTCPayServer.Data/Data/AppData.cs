using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class AppData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string StoreDataId { get; set; }
        public string AppType { get; set; }
        public StoreData StoreData { get; set; }
        public DateTimeOffset Created { get; set; }
        public bool TagAllInvoices { get; set; }
        public string Settings { get; set; }
        public bool Archived { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<AppData>()
                       .HasOne(o => o.StoreData)
                       .WithMany(i => i.Apps).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<AppData>()
                    .HasOne(a => a.StoreData);

            builder.Entity<AppData>()
                .Property(o => o.Settings)
                .HasColumnType("JSONB");
        }

        // utility methods
        public T GetSettings<T>() where T : class, new()
        {
            return string.IsNullOrEmpty(Settings) ? new T() : JsonConvert.DeserializeObject<T>(Settings);
        }

        public void SetSettings(object value)
        {
            Settings = value == null ? null : JsonConvert.SerializeObject(value);
        }
    }
}
