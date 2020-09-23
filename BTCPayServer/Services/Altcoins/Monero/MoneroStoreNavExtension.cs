#if ALTCOINS
using BTCPayServer.Contracts;

namespace BTCPayServer.Services.Altcoins.Monero
{
    public class MoneroNavExtension : INavExtension
    {
        public string Partial { get; } = "Monero/StoreNavMoneroExtension";
        public string Location { get; } = "store";
    }
}
#endif
