#nullable enable
using System.Collections.Generic;

namespace BTCPayServer.Client.App.Models;

public class CreateStoreData
{
    public string? DefaultCurrency { get; set; }
    public string? RecommendedExchangeId { get; set; }
    public Dictionary<string, string>? Exchanges { get; set; }
}
