using BTCPayServer.Contracts;

namespace BTCPayServer.Shitcoins.Monero
{
    public class MoneroStoreNavExtension: IStoreNavExtension
    {
        public string Partial { get; } = "Monero/StoreNavMoneroExtension";
    }
}
