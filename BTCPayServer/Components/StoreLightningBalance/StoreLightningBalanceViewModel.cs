using System;
using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Wallets;
using NBitcoin;

namespace BTCPayServer.Components.StoreLightningBalance;

public class StoreLightningBalanceViewModel
{
    public string StoreId { get; set; }
    public string CryptoCode { get; set; }
    public string DefaultCurrency { get; set; }
    public CurrencyData CurrencyData { get; set; }
    public StoreData Store { get; set; }
    public Money TotalOnchain { get; set; }
    public LightMoney TotalOffchain { get; set; }
    public LightningNodeBalance Balance { get; set; }
    public string ProblemDescription { get; set; }
    public bool InitialRendering { get; set; } = true;
    public WalletHistogramType Type { get; set; }
    public IList<DateTimeOffset> Labels { get; set; }
    public IList<decimal> Series { get; set; }
    public string DataUrl { get; set; }
}
