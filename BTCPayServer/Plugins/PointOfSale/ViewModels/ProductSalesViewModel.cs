using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.PointOfSale.ViewModels;

public class ProductSalesViewModel
{
    public string AppId { get; set; }
    public string AppName { get; set; }
    public List<AppItemStats> Items { get; set; }
}
