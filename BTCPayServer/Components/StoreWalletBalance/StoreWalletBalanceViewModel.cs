using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Wallets;

namespace BTCPayServer.Components.StoreWalletBalance;

public class StoreWalletBalanceViewModel
{
    public decimal? Balance { get; set; }
    public string CryptoCode { get; set; }
    public string DefaultCurrency { get; set; }
    public CurrencyData CurrencyData { get; set; }
    public StoreData Store { get; set; }
    public WalletId WalletId { get; set; }
    public WalletHistogramType Type { get; set; }
    public IList<string> Labels { get; set; }
    public IList<decimal> Series { get; set; }
}
