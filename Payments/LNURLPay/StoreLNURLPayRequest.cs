#nullable enable
using BTCPayServer.Data;
using LNURL;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.LNURLPay;

public class StoreLNURLPayRequest : LNURLPayRequest
{
    [JsonIgnore]
    public StoreData? Store { get; set; }
}
