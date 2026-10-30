namespace BTCPayServer.Services
{
    public class MigrationSettings
    {
        // Done in DbMigrationsHostedService
        public int? MigratedInvoiceTextSearchPages { get; set; }
        public int? MigratedTransactionLabels { get; set; }
        public bool MigrateToStoreConfig { get; set; }
        public bool MigrateBlockExplorerLinks { get; set; }
        public bool MigrateStoreExcludedPaymentMethods { get; set; }
        public bool MigrateOldDerivationSchemes { get; set; }
    }
}
