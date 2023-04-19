using BTCPayServer.Abstractions.Custodians.Client;

namespace BTCPayServer.Models.CustodianAccountViewModels;

public class TradePrepareViewModel : AssetQuoteResult
{
    public decimal MaxQty { get; set; }

}
