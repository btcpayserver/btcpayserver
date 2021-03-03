#if ALTCOINS
using BTCPayServer.Contracts;

namespace BTCPayServer.Services.Altcoins.Matic
{
    public class MaticStoreNavExtension: IStoreNavExtension
    {
        public string Partial { get; } = "Matic/StoreNavMaticExtension";
    }
}
#endif
