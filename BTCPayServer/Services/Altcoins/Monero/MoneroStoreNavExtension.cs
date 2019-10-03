using BTCPayServer.Contracts;

namespace BTCPayServer.Services.Altcoins.Monero
{
    public class MoneroStoreNavExtension: IStoreNavExtension
    {
        public string Partial { get; } = "Monero/StoreNavMoneroExtension";
    }
}
