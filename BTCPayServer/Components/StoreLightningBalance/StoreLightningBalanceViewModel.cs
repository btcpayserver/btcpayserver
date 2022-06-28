using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;

namespace BTCPayServer.Components.StoreLightningBalance;

public class StoreLightningBalanceViewModel
{
    public string CryptoCode { get; set; }
    public string DefaultCurrency { get; set; }
    public CurrencyData CurrencyData { get; set; }
    public StoreData Store { get; set; }
    
    [JsonConverter(typeof (LightMoneyJsonConverter))]
    public LightMoney TotalOnchain { get; set; }
    
    [JsonConverter(typeof (LightMoneyJsonConverter))]
    public LightMoney TotalOffchain { get; set; }
    public LightningNodeBalance Balance { get; set; }
    public string ProblemDescription { get; set; }
}
