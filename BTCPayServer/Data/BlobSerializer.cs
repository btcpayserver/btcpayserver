#nullable enable
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Data
{
    public static class BlobSerializer
    {
        public static (JsonSerializerSettings SerializerSettings, JsonSerializer Serializer) CreateSerializer()
        {
            JsonSerializerSettings settings = CreateSettings();
            return (settings, JsonSerializer.CreateDefault(settings));
        }
        public static (JsonSerializerSettings SerializerSettings, JsonSerializer Serializer) CreateSerializer(Network? network)
        {
            JsonSerializerSettings settings = CreateSettings();
            NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(settings, network);
            settings.ContractResolver = CreateResolver();
            return (settings, JsonSerializer.CreateDefault(settings));
        }
        public static (JsonSerializerSettings SerializerSettings, JsonSerializer Serializer) CreateSerializer(NBXplorerNetwork network)
        {
            JsonSerializerSettings settings = CreateSettings();
            network.Serializer.ConfigureSerializer(settings);
            settings.ContractResolver = CreateResolver();
            return (settings, JsonSerializer.CreateDefault(settings));
        }

        private static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings()
            {
                ContractResolver = CreateResolver(),
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
            };
            return settings;
        }

        private static CamelCasePropertyNamesContractResolver CreateResolver()
        {
            return new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false
                }
            };
        }
    }
}
