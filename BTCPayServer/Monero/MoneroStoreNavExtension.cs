using BTCPayServer.Contracts;

namespace BTCPayServer.Monero
{
    public class MoneroStoreNavExtension: IStoreNavExtension
    {
        public string Partial { get; } = "Monero/StoreNavMoneroExtension";
    }
}
