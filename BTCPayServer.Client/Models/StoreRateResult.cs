using System.Collections.Generic;

namespace BTCPayServer.Client.Models;

public class StoreRateResult
{
    public string CurrencyPair { get; set; }
    public decimal? Rate { get; set; }
    public List<string> Errors { get; set; }
}
