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
        var original = custodianAccountData.GetBlob();
        if (JToken.DeepEquals(original, blob))
            return false;
        custodianAccountData.Blob = blob is null ? null : ZipUtils.Zip(blob.ToString(Newtonsoft.Json.Formatting.None));
        return true;
    }
}
