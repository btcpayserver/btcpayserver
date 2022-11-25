using Newtonsoft.Json;

namespace BTCPayServer.Abstractions
{
    public class CamelCaseSerializerSettings
    {
        static CamelCaseSerializerSettings()
        {
            Settings = new JsonSerializerSettings()
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };
            Serializer = JsonSerializer.Create(Settings);
        }
        public static readonly JsonSerializerSettings Settings;
        public static readonly JsonSerializer Serializer;
    }
}
