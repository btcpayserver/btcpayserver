using BTCPayServer.Contracts;

namespace BTCPayServer.Altcoins.Monero
{
    public class MoneroStoreNavExtension: IStoreNavExtension
    {
        public string Partial { get; } = "Monero/StoreNavMoneroExtension";
    }
}
