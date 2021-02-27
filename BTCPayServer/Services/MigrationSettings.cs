namespace BTCPayServer.Services
{
    public class MigrationSettings
    {
        public bool UnreachableStoreCheck { get; set; }
        public bool DeprecatedLightningConnectionStringCheck { get; set; }
        public bool ConvertMultiplierToSpread { get; set; }
        public bool ConvertNetworkFeeProperty { get; set; }
        public bool ConvertCrowdfundOldSettings { get; set; }
        public bool ConvertWalletKeyPathRoots { get; set; }
        public bool CheckedFirstRun { get; set; }
        public bool PaymentMethodCriteria { get; set; }
        public bool TransitionToStoreBlobAdditionalData { get; set; }
        public bool TransitionInternalNodeConnectionString { get; set; }

        public override string ToString()
        {
            return string.Empty;
        }
        
        // Done in DbMigrationsHostedService
        public int? MigratedInvoiceTextSearchPages { get; set; }
    }
}
