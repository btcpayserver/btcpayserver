using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Lightning;
using BTCPayServer.Plugins.LNbank.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.LNbank
{
    public class LNbankPluginDbContext : DbContext
    {
        private readonly bool _designTime;

        public LNbankPluginDbContext(DbContextOptions<LNbankPluginDbContext> options, bool designTime = false)
            : base(options)
        {
            _designTime = designTime;
        }
        
        public DbSet<Wallet> Wallets { get; set; }
        public DbSet<AccessKey> AccessKeys { get; set; }
        public DbSet<Transaction> Transactions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema("BTCPayServer.Plugins.LNbank");
            if (Database.IsSqlite() && !_designTime)
            {
                // SQLite does not have proper support for DateTimeOffset via Entity Framework Core, see the limitations
                // here: https://docs.microsoft.com/en-us/ef/core/providers/sqlite/limitations#query-limitations
                // To work around this, when the Sqlite database provider is used, all model properties of type DateTimeOffset
                // use the DateTimeOffsetToBinaryConverter
                // Based on: https://github.com/aspnet/EntityFrameworkCore/issues/10784#issuecomment-415769754
                // This only supports millisecond precision, but should be sufficient for most use cases.
                foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                {
                    var properties = entityType.ClrType.GetProperties()
                        .Where(p => p.PropertyType == typeof(DateTimeOffset));
                    foreach (var property in properties)
                    {
                        modelBuilder
                            .Entity(entityType.Name)
                            .Property(property.Name)
                            .HasConversion(
                                new Microsoft.EntityFrameworkCore.Storage.ValueConversion.
                                    DateTimeOffsetToBinaryConverter());
                    }
                }
            }
            
            modelBuilder.Entity<Wallet>().HasIndex(o => o.UserId);
            modelBuilder.Entity<AccessKey>().HasIndex(o => o.WalletId);
            modelBuilder.Entity<Transaction>().HasIndex(o => o.InvoiceId);
            modelBuilder.Entity<Transaction>().HasIndex(o => o.WalletId);
            
            modelBuilder
                .Entity<AccessKey>()
                .HasOne(o => o.Wallet)
                .WithMany(w => w.AccessKeys)
                .OnDelete(DeleteBehavior.Cascade);    
            
            modelBuilder
                .Entity<Transaction>()
                .HasOne(o => o.Wallet)
                .WithMany(w => w.Transactions)
                .OnDelete(DeleteBehavior.Cascade);
            
            modelBuilder
                .Entity<Transaction>()
                .Property(e => e.Amount)
                .HasConversion(
                    v => v.MilliSatoshi,
                    v => new LightMoney(v));

            modelBuilder
                .Entity<Transaction>()
                .Property(e => e.AmountSettled)
                .HasConversion(
                    v => v.MilliSatoshi,
                    v => new LightMoney(v));
        }
    }

    public class AccessKey
    {
        [Key]
        public string Key { get; set; }
        
        public string WalletId { get; set; }
        public Wallet Wallet { get; set; }
    }
}
