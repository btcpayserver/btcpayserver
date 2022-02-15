using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

public static class CustodianAccountDataExtensions
{
    public static CustodianAccountData.CustodianAccountBlob GetBlob(this CustodianAccountData custodianAccountData)
    {
        var result = custodianAccountData.Blob == null
            ? new CustodianAccountData.CustodianAccountBlob()
            : JObject.Parse(ZipUtils.Unzip(custodianAccountData.Blob)).ToObject<CustodianAccountData.CustodianAccountBlob>();
        return result;
    }

    public static bool SetBlob(this CustodianAccountData custodianAccountData, CustodianAccountData.CustodianAccountBlob blob)
    {
        var original = new Serializer(null).ToString(custodianAccountData.GetBlob());
        var newBlob = new Serializer(null).ToString(blob);
        if (original == newBlob)
            return false;
        custodianAccountData.Blob = ZipUtils.Zip(newBlob);
        return true;
    }
}
