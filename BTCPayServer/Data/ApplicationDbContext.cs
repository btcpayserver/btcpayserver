using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Models;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext()
        {

        }
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<InvoiceData> Invoices
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
                .HasIndex(o => o.StoreDataId);

            builder.Entity<PaymentData>()
                .HasIndex(o => o.InvoiceDataId);

            builder.Entity<RefundAddressesData>()
                .HasIndex(o => o.InvoiceDataId);

            builder.Entity<UserStore>()
                   .HasKey(t => new
                   {
                       t.ApplicationUserId,
                       t.StoreDataId
                   });

            builder.Entity<UserStore>()
                 .HasOne(pt => pt.ApplicationUser)
                 .WithMany(p => p.UserStores)
                 .HasForeignKey(pt => pt.ApplicationUserId);

            builder.Entity<UserStore>()
                .HasOne(pt => pt.StoreData)
                .WithMany(t => t.UserStores)
                .HasForeignKey(pt => pt.StoreDataId);

            builder.Entity<AddressInvoiceData>()
                .HasKey(o => o.Address);

            builder.Entity<PairingCodeData>()
                .HasKey(o => o.Id);

            builder.Entity<PairedSINData>(b =>
            {
                b.HasIndex(o => o.SIN);
                b.HasIndex(o => o.StoreDataId);
            });

            builder.Entity<HistoricalAddressInvoiceData>()
                .HasKey(o => new
                {
                    o.InvoiceDataId,
                    o.Address
                });
        }
    }
}
