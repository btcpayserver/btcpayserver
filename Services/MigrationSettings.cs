using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class MigrationSettings
    {
        [JsonProperty("MigrateHotwalletProperty2")]
        public bool MigrateHotwalletProperty { get; set; }
        public bool MigrateU2FToFIDO2 { get; set; }
        public bool PaymentMethodCriteria { get; set; }
        public bool TransitionToStoreBlobAdditionalData { get; set; }
        public bool TransitionInternalNodeConnectionString { get; set; }

        // Done in DbMigrationsHostedService
        public int? MigratedInvoiceTextSearchPages { get; set; }
        public int? MigratedTransactionLabels { get; set; }
        public bool MigrateAppCustomOption { get; set; }
        public bool MigratePayoutDestinationId { get; set; }
        public bool AddInitialUserBlob { get; set; }
        public bool LighingAddressSettingRename { get; set; }
        public bool LighingAddressDatabaseMigration { get; set; }
        public bool AddStoreToPayout { get; set; }
        public bool MigrateEmailServerDisableTLSCerts { get; set; }
        public bool MigrateWalletColors { get; set; }
        public bool FileSystemStorageAsDefault { get; set; }
        public bool FixMappedDomainAppType { get; set; }
        public bool MigrateAppYmlToJson { get; set; }
        public bool MigrateToStoreConfig { get; set; }
        public bool MigrateBlockExplorerLinks { get; set; }
        public bool MigrateStoreExcludedPaymentMethods { get; set; }
        public bool MigrateOldDerivationSchemes { get; set; }
    }
}
