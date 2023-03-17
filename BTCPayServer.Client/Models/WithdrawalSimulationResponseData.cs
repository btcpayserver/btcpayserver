using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class WithdrawalSimulationResponseData : WithdrawalBaseResponseData
{
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? MinQty { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? MaxQty { get; set; }

    public WithdrawalSimulationResponseData(string paymentMethod, string asset, string accountId,
        string custodianCode, List<LedgerEntryData> ledgerEntries, decimal? minQty, decimal? maxQty) : base(paymentMethod,
        asset, ledgerEntries, accountId, custodianCode)
    {
        MinQty = minQty;
        MaxQty = maxQty;
    }
}
