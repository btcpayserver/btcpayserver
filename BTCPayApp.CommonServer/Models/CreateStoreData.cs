using System.Collections.Generic;

namespace BTCPayApp.CommonServer.Models;

public class CreateStoreData
{
    public string? DefaultCurrency { get; set; }
    public string? RecommendedExchangeId { get; set; }
    public Dictionary<string, string>? Exchanges { get; set; }
}
