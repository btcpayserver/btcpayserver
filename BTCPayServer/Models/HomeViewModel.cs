using BTCPayServer.Data;

namespace BTCPayServer.Models;

public class HomeViewModel
{
    public bool HasStore { get; set; }
    public bool HasStoreWithWallet { get; set; }
    public StoreData StoreWithoutPaymentMethod { get; set; }
}
