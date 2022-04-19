using System.Linq;
using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class APIKeyDataExtensions
    {
        public static APIKeyBlob GetBlob(this APIKeyData apiKeyData)
        {
            return GetBlob<APIKeyBlob>(apiKeyData.Blob);
        }

        public static bool SetBlob(this APIKeyData apiKeyData, APIKeyBlob blob)
        {
            var newBlob = SerializeBlob(blob);
            if (apiKeyData?.Blob?.SequenceEqual(newBlob) is true)
                return false;
            apiKeyData.Blob = newBlob;
            return true;
        }
        
        public static T GetBlob<T>(this byte[] data)
        {
            var result = data == null
                ? default
                : JObject.Parse(ZipUtils.Unzip(data)).ToObject<T>();
            return result;
        }

        public static byte[] SerializeBlob<T>(this T blob)
        {
            var newBlob = new Serializer(null).ToString(blob);
            return ZipUtils.Zip(newBlob);
        }
    }
}
