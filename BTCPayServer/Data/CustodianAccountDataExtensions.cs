#nullable enable
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

public static class CustodianAccountDataExtensions
{
    public static JObject GetBlob(this CustodianAccountData custodianAccountData)
    {
        return ((IHasBlob<JObject>)custodianAccountData).GetBlob() ?? new JObject();
    }
}
