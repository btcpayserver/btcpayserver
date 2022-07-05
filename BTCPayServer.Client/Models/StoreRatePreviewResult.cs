using System.Collections.Generic;

namespace BTCPayServer.Client.Models;

public class StoreRatePreviewResult
{
    public string CurrencyPair { get; set; }
    public decimal? Rate { get; set; }
    public List<string> Errors { get; set; }
}