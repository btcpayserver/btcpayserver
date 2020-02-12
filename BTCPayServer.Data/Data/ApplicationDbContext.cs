using System;
using System.Linq;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenIddict.EntityFrameworkCore.Models;

namespace BTCPayServer.Data
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
 
            builder.UseSqlite("Data Source=temp.db");
 
            return new ApplicationDbContext(builder.Options, true);
        }
    }
    
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        private readonly bool _designTime;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, bool designTime = false)
            : base(options)
        {
            _designTime = designTime;
        }

        public DbSet<InvoiceData> Invoices
        {
            get; set;
        }

        public DbSet<AppData> Apps
        {
            get; set;
        }

        public DbSet<InvoiceEventData> InvoiceEvents
        {
            get; set;
        }

        public DbSet<HistoricalAddressInvoiceData> HistoricalAddressInvoices
        {
            get; set;
        }

        public DbSet<PendingInvoiceData> PendingInvoices
        {
            get; set;
        }
        public DbSet<RefundAddressesData> RefundAddresses
        {
            get; set;
        }

        public DbSet<PaymentData> Payments
        {
            get; set;
        }
        
        public DbSet<PaymentRequestData> PaymentRequests
        {
            get; set;
        }

        public DbSet<WalletData> Wallets { get; set; }
        public DbSet<WalletTransactionData> WalletTransactions { get; set; }

        public DbSet<StoreData> Stores
        {
            get; set;
        }

        public DbSet<UserStore> UserStore
        {
            get; set;
        }

        public DbSet<AddressInvoiceData> AddressInvoices
        {
            get; set;
        }

        public DbSet<SettingData> Settings
        {
            get; set;
        }


        public DbSet<PairingCodeData> PairingCodes
        {
            get; set;
        }

        public DbSet<PairedSINData> PairedSINData
        {
            get; set;
        }

        public DbSet<APIKeyData> ApiKeys
        {
            get; set;
        } 
        
        public DbSet<StoredFile> Files
        {
            get; set;
        }
       

        public DbSet<U2FDevice> U2FDevices { get; set; }   
        
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var isConfigured = optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().Any();
            if (!isConfigured)
                optionsBuilder.UseSqlite("Data Source=temp.db");
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<InvoiceData>()
                .HasOne(o => o.StoreData)
                .WithMany(a => a.Invoices).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<InvoiceData>().HasIndex(o => o.StoreDataId);


            builder.Entity<PaymentData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.Payments).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PaymentData>()
                   .HasIndex(o => o.InvoiceDataId);


            builder.Entity<RefundAddressesData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.RefundAddresses).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<RefundAddressesData>()
                .HasIndex(o => o.InvoiceDataId);

            builder.Entity<UserStore>()
                   .HasOne(o => o.StoreData)
                   .WithMany(i => i.UserStores).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<UserStore>()
                   .HasKey(t => new
                   {
                       t.ApplicationUserId,
                       t.StoreDataId
                   });

            builder.Entity<APIKeyData>()
                   .HasOne(o => o.StoreData)
                   .WithMany(i => i.APIKeys)
                   .HasForeignKey(i => i.StoreId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<APIKeyData>()
                .HasIndex(o => o.StoreId);

            builder.Entity<AppData>()
                   .HasOne(o => o.StoreData)
                   .WithMany(i => i.Apps).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<AppData>()
                    .HasOne(a => a.StoreData);

            builder.Entity<UserStore>()
                 .HasOne(pt => pt.ApplicationUser)
                 .WithMany(p => p.UserStores)
                 .HasForeignKey(pt => pt.ApplicationUserId);

            builder.Entity<UserStore>()
                .HasOne(pt => pt.StoreData)
                .WithMany(t => t.UserStores)
                .HasForeignKey(pt => pt.StoreDataId);


            builder.Entity<AddressInvoiceData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.AddressInvoices).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<AddressInvoiceData>()
#pragma warning disable CS0618
                .HasKey(o => o.Address);
#pragma warning restore CS0618

            builder.Entity<PairingCodeData>()
                .HasKey(o => o.Id);

            builder.Entity<PendingInvoiceData>()
                .HasOne(o => o.InvoiceData)
                .WithMany(o => o.PendingInvoices)
                .HasForeignKey(o => o.Id).OnDelete(DeleteBehavior.Cascade);


            builder.Entity<PairedSINData>()
                   .HasOne(o => o.StoreData)
                   .WithMany(i => i.PairedSINs).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PairedSINData>(b =>
            {
                b.HasIndex(o => o.SIN);
                b.HasIndex(o => o.StoreDataId);
            });

            builder.Entity<HistoricalAddressInvoiceData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.HistoricalAddressInvoices).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<HistoricalAddressInvoiceData>()
                .HasKey(o => new
                {
                    o.InvoiceDataId,
#pragma warning disable CS0618
                    o.Address
#pragma warning restore CS0618
                });


            builder.Entity<InvoiceEventData>()
                   .HasOne(o => o.InvoiceData)
                   .WithMany(i => i.Events).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<InvoiceEventData>()
                .HasKey(o => new
                {
                    o.InvoiceDataId,
#pragma warning disable CS0618
                    o.UniqueId
#pragma warning restore CS0618
                });
            
            
            builder.Entity<PaymentRequestData>()
                .HasOne(o => o.StoreData)
                .WithMany(i => i.PaymentRequests)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PaymentRequestData>()
                .Property(e => e.Created)
                .HasDefaultValue(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero));

            builder.Entity<PaymentRequestData>()
                .HasIndex(o => o.Status);

            builder.Entity<WalletTransactionData>()
                .HasKey(o => new
                {
                    o.WalletDataId,
#pragma warning disable CS0618
                    o.TransactionId
#pragma warning restore CS0618
                });
            builder.Entity<WalletTransactionData>()
                .HasOne(o => o.WalletData)
                .WithMany(w => w.WalletTransactions).OnDelete(DeleteBehavior.Cascade);

            builder.UseOpenIddict<BTCPayOpenIdClient, BTCPayOpenIdAuthorization, OpenIddictScope<string>, BTCPayOpenIdToken, string>();

            if (Database.IsSqlite() && !_designTime)
            {
                // SQLite does not have proper support for DateTimeOffset via Entity Framework Core, see the limitations
                // here: https://docs.microsoft.com/en-us/ef/core/providers/sqlite/limitations#query-limitations
                // To work around this, when the Sqlite database provider is used, all model properties of type DateTimeOffset
                // use the DateTimeOffsetToBinaryConverter
                // Based on: https://github.com/aspnet/EntityFrameworkCore/issues/10784#issuecomment-415769754
                // This only supports millisecond precision, but should be sufficient for most use cases.
                foreach (var entityType in builder.Model.GetEntityTypes())
                {
                    var properties = entityType.ClrType.GetProperties().Where(p => p.PropertyType == typeof(DateTimeOffset));
                    foreach (var property in properties)
                    {
                        builder
                            .Entity(entityType.Name)
                            .Property(property.Name)
                            .HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
                    }
                }
            }
        }
    }

}
