using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Invoices;

[JsonObject(ItemNullValueHandling  = NullValueHandling.Ignore)]
public class PosReceiptData
{
    public string Description { get; set; }
    public string Title { get; set; }
    public Dictionary<string, string> Cart { get; set; }
    public string Subtotal { get; set; }
    public string Discount { get; set; }
    public string Tip { get; set; }
    public string Total { get; set; }
    public string ItemsTotal { get; set; }
    public string Tax { get; set; }
}
