using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins
{
    public class PublishedVersion
    {
        public string ProjectSlug { get; set; }
        public long BuildId { get; set; }
        public JObject BuildInfo { get; set; }
        public JObject ManifestInfo { get; set; }
        public string Documentation { get; set; }
    }
    public class PluginBuilderClient
    {
        HttpClient httpClient;
        public HttpClient HttpClient => httpClient;
        public PluginBuilderClient(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }
        static JsonSerializerSettings serializerSettings = new JsonSerializerSettings() { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() };
        public async Task<PublishedVersion[]> GetPublishedVersions(string btcpayVersion, bool includePreRelease)
        {
            var result = await httpClient.GetStringAsync($"api/v1/plugins?btcpayVersion={btcpayVersion}&includePreRelease={includePreRelease}");
            return JsonConvert.DeserializeObject<PublishedVersion[]>(result, serializerSettings) ?? throw new InvalidOperationException();
        }
    }
}
