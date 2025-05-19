using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Invoices;
public class WellKnownPosData
{
    public static WellKnownPosData TryParse(Dictionary<string, object> data)
    {
        try
        {
            return JObject.FromObject(data).ToObject<WellKnownPosData>();
        }
        catch
        {
        }
        return null;
    }

    public static bool IsWellKnown(string field)
    => field.ToLowerInvariant() is "cart" or "subtotal" or "discount" or "tip" or "total" or "tax" or "itemstotal" or "discountamount";
    public object Subtotal { get; set; }
    public object Discount { get; set; }
    public object Tip { get; set; }
    public object Total { get; set; }
    public object ItemsTotal { get; set; }
    public object Tax { get; set; }
}
