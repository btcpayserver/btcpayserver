using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Components.StoreWalletBalance;

public class StoreWalletBalanceViewModel
{
    public string StoreId { get; set; }
    public decimal? Balance { get; set; }
    public string CryptoCode { get; set; }
    public string DefaultCurrency { get; set; }
    public CurrencyData CurrencyData { get; set; }
    public WalletId WalletId { get; set; }
    public HistogramType Type { get; set; }
    public IList<DateTimeOffset> Labels { get; set; }
    public IList<decimal> Series { get; set; }
    public bool MissingWalletConfig { get; set; }
}
