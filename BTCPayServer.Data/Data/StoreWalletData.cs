namespace BTCPayServer.Data
{
    public class StoreWalletData
    {
        public string StoreDataId { get; set; }
        public string WalletDataId { get; set; }
        
        public WalletData WalletData { get; set; }
        public StoreData StoreData { get; set; }
    }
}
