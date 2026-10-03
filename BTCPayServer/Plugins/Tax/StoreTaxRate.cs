using System;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Plugins.GlobalSearch.Views;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer;

public class StoreTaxRate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public decimal Rate { get; set; }
    public bool IsDefault { get; set; }
}
