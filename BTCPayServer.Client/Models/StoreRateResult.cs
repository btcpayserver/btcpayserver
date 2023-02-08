using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class StoreRateResult
{
    public string CurrencyPair { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? Rate { get; set; }
    public List<string> Errors { get; set; }
}
