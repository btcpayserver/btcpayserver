using System;
using System.Linq;
using System.Threading.Tasks;
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
#nullable enable
        public async Task<string?> GetMigrationState()
        {
            return (await Settings.FromSqlRaw("SELECT \"Id\", \"Value\" FROM \"Settings\" WHERE \"Id\"='MigrationData'").AsNoTracking().FirstOrDefaultAsync())?.Value;
        }
#nullable restore
        public DbSet<AddressInvoiceData> AddressInvoices { get; set; }
        public DbSet<APIKeyData> ApiKeys { get; set; }
        public DbSet<AppData> Apps { get; set; }
        public DbSet<CustodianAccountData> CustodianAccount { get; set; }
        public DbSet<StoredFile> Files { get; set; }
        public DbSet<InvoiceEventData> InvoiceEvents { get; set; }
        public DbSet<InvoiceSearchData> InvoiceSearches { get; set; }
        public DbSet<InvoiceWebhookDeliveryData> InvoiceWebhookDeliveries { get; set; }
        public DbSet<InvoiceData> Invoices { get; set; }
        public DbSet<NotificationData> Notifications { get; set; }
        public DbSet<OffchainTransactionData> OffchainTransactions { get; set; }
        public DbSet<PairedSINData> PairedSINData { get; set; }
        public DbSet<PairingCodeData> PairingCodes { get; set; }
        public DbSet<PayjoinLock> PayjoinLocks { get; set; }
        public DbSet<PaymentRequestData> PaymentRequests { get; set; }
        public DbSet<PaymentData> Payments { get; set; }
        public DbSet<PayoutData> Payouts { get; set; }
        public DbSet<PendingInvoiceData> PendingInvoices { get; set; }
        public DbSet<PlannedTransaction> PlannedTransactions { get; set; }
        public DbSet<PullPaymentData> PullPayments { get; set; }
        public DbSet<RefundData> Refunds { get; set; }
        public DbSet<SettingData> Settings { get; set; }
        public DbSet<StoreSettingData> StoreSettings { get; set; }
        public DbSet<StoreWebhookData> StoreWebhooks { get; set; }
        public DbSet<StoreData> Stores { get; set; }
        public DbSet<U2FDevice> U2FDevices { get; set; }
        public DbSet<Fido2Credential> Fido2Credentials { get; set; }
        public DbSet<UserStore> UserStore { get; set; }
        public DbSet<StoreRole> StoreRoles { get; set; }
        [Obsolete]
        public DbSet<WalletData> Wallets { get; set; }
        public DbSet<WalletObjectData> WalletObjects { get; set; }
        public DbSet<WalletObjectLinkData> WalletObjectLinks { get; set; }
        [Obsolete]
        public DbSet<WalletTransactionData> WalletTransactions { get; set; }
        public DbSet<WebhookDeliveryData> WebhookDeliveries { get; set; }
        public DbSet<WebhookData> Webhooks { get; set; }
        public DbSet<LightningAddressData> LightningAddresses { get; set; }
        public DbSet<PayoutProcessorData> PayoutProcessors { get; set; }
        public DbSet<FormData> Forms { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var isConfigured = optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().Any();
            if (!isConfigured)
                optionsBuilder.UseSqlite("Data Source=temp.db");
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // some of the data models don't have OnModelCreating for now, commenting them

            ApplicationUser.OnModelCreating(builder, Database);
            AddressInvoiceData.OnModelCreating(builder);
            APIKeyData.OnModelCreating(builder, Database);
            AppData.OnModelCreating(builder);
            CustodianAccountData.OnModelCreating(builder, Database);
            //StoredFile.OnModelCreating(builder);
            InvoiceEventData.OnModelCreating(builder);
            InvoiceSearchData.OnModelCreating(builder);
            InvoiceWebhookDeliveryData.OnModelCreating(builder);
            InvoiceData.OnModelCreating(builder, Database);
            NotificationData.OnModelCreating(builder, Database);
            //OffchainTransactionData.OnModelCreating(builder);
            BTCPayServer.Data.PairedSINData.OnModelCreating(builder);
            PairingCodeData.OnModelCreating(builder);
            //PayjoinLock.OnModelCreating(builder);
            PaymentRequestData.OnModelCreating(builder, Database);
            PaymentData.OnModelCreating(builder, Database);
            PayoutData.OnModelCreating(builder);
            PendingInvoiceData.OnModelCreating(builder);
            //PlannedTransaction.OnModelCreating(builder);
            PullPaymentData.OnModelCreating(builder);
            RefundData.OnModelCreating(builder);
            SettingData.OnModelCreating(builder, Database);
            StoreSettingData.OnModelCreating(builder, Database);
            StoreWebhookData.OnModelCreating(builder);
            StoreData.OnModelCreating(builder, Database);
            U2FDevice.OnModelCreating(builder);
            Fido2Credential.OnModelCreating(builder, Database);
            BTCPayServer.Data.UserStore.OnModelCreating(builder);
            //WalletData.OnModelCreating(builder);
            WalletObjectData.OnModelCreating(builder, Database);
            WalletObjectLinkData.OnModelCreating(builder, Database);
#pragma warning disable CS0612 // Type or member is obsolete
            WalletTransactionData.OnModelCreating(builder);
#pragma warning restore CS0612 // Type or member is obsolete
            WebhookDeliveryData.OnModelCreating(builder, Database);
            LightningAddressData.OnModelCreating(builder, Database);
            PayoutProcessorData.OnModelCreating(builder, Database);
            WebhookData.OnModelCreating(builder, Database);
            FormData.OnModelCreating(builder, Database);
            StoreRole.OnModelCreating(builder, Database);


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
