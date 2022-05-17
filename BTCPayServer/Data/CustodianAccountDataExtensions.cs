using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

public static class CustodianAccountDataExtensions
{
    public static JObject GetBlob(this CustodianAccountData custodianAccountData)
    {
        var result = custodianAccountData.Blob == null
            ? new JObject()
            : JObject.Parse(ZipUtils.Unzip(custodianAccountData.Blob));
        return result;
    }

    public static bool SetBlob(this CustodianAccountData custodianAccountData, JObject blob)
    {
        var original = new Serializer(null).ToString(custodianAccountData.GetBlob());
        var newBlob = new Serializer(null).ToString(blob);
        if (original == newBlob)
            return false;
        custodianAccountData.Blob = ZipUtils.Zip(newBlob);
        return true;
    }
}
