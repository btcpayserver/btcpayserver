using System;
using System.Linq;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

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
        public DbSet<RefundData> Refunds
        {
            get; set;
        }

        public DbSet<PlannedTransaction> PlannedTransactions { get; set; }
        public DbSet<PayjoinLock> PayjoinLocks { get; set; }
        public DbSet<AppData> Apps { get; set; }
        public DbSet<InvoiceEventData> InvoiceEvents { get; set; }
        public DbSet<OffchainTransactionData> OffchainTransactions { get; set; }
        public DbSet<HistoricalAddressInvoiceData> HistoricalAddressInvoices { get; set; }
        public DbSet<PendingInvoiceData> PendingInvoices { get; set; }
        public DbSet<PaymentData> Payments { get; set; }
        public DbSet<PaymentRequestData> PaymentRequests { get; set; }
        public DbSet<PullPaymentData> PullPayments { get; set; }
        public DbSet<PayoutData> Payouts { get; set; }
        public DbSet<WalletData> Wallets { get; set; }
        public DbSet<WalletTransactionData> WalletTransactions { get; set; }
        public DbSet<StoreData> Stores { get; set; }
        public DbSet<UserStore> UserStore { get; set; }
        public DbSet<AddressInvoiceData> AddressInvoices { get; set; }
        public DbSet<SettingData> Settings { get; set; }
        public DbSet<PairingCodeData> PairingCodes { get; set; }
        public DbSet<PairedSINData> PairedSINData { get; set; }
        public DbSet<APIKeyData> ApiKeys { get; set; }
        public DbSet<StoredFile> Files { get; set; }
        public DbSet<U2FDevice> U2FDevices { get; set; }
        public DbSet<NotificationData> Notifications { get; set; }

        public DbSet<StoreWebhookData> StoreWebhooks { get; set; }
        public DbSet<WebhookData> Webhooks { get; set; }
        public DbSet<WebhookDeliveryData> WebhookDeliveries { get; set; }
        public DbSet<InvoiceWebhookDeliveryData> InvoiceWebhookDeliveries { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var isConfigured = optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().Any();
            if (!isConfigured)
                optionsBuilder.UseSqlite("Data Source=temp.db");
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            Data.UserStore.OnModelCreating(builder);
            NotificationData.OnModelCreating(builder);
            InvoiceData.OnModelCreating(builder);
            PaymentData.OnModelCreating(builder);
            Data.UserStore.OnModelCreating(builder);
            APIKeyData.OnModelCreating(builder);
            AppData.OnModelCreating(builder);
            AddressInvoiceData.OnModelCreating(builder);
            PairingCodeData.OnModelCreating(builder);
            PendingInvoiceData.OnModelCreating(builder);
            Data.PairedSINData.OnModelCreating(builder);
            HistoricalAddressInvoiceData.OnModelCreating(builder);
            InvoiceEventData.OnModelCreating(builder);
            PaymentRequestData.OnModelCreating(builder);
            WalletTransactionData.OnModelCreating(builder);
            PullPaymentData.OnModelCreating(builder);
            PayoutData.OnModelCreating(builder);
            RefundData.OnModelCreating(builder);
            U2FDevice.OnModelCreating(builder);

            Data.WebhookDeliveryData.OnModelCreating(builder);
            Data.StoreWebhookData.OnModelCreating(builder);
            Data.InvoiceWebhookDeliveryData.OnModelCreating(builder);

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
