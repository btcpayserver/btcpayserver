using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class APIKeyDataExtensions
    {
        public static APIKeyBlob GetBlob(this APIKeyData apiKeyData)
        {
            var result = apiKeyData.Blob == null
                ? new APIKeyBlob()
                : JObject.Parse(ZipUtils.Unzip(apiKeyData.Blob)).ToObject<APIKeyBlob>();
            return result;
        }

        public static bool SetBlob(this APIKeyData apiKeyData, APIKeyBlob blob)
        {
            var original = new Serializer(null).ToString(apiKeyData.GetBlob());
            var newBlob = new Serializer(null).ToString(blob);
            if (original == newBlob)
                return false;
            apiKeyData.Blob = ZipUtils.Zip(newBlob);
            return true;
        }
    }
}
