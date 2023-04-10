using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ExchangeSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Net.WebRequestMethods;

namespace BTCPayServer.Plugins
{
    public class PublishedVersion
    {
        public class BuildInfoClass
        {
            public string gitCommit { get; set; }
            public string pluginDir { get; set; }
            public string gitRepository { get; set; }
#nullable enable
            static Regex GithubRepositoryRegex = new Regex("^https://(www\\.)?github\\.com/([^/]+)/([^/]+)/?");
            public record GithubRepository(string Owner, string RepositoryName)
            {
                public string? GetSourceUrl(string commit, string pluginDir)
                {
                    if (commit is null)
                        return null;
                    return $"https://github.com/{Owner}/{RepositoryName}/tree/{commit}/{pluginDir}";
                }
            }
            public GithubRepository? GetGithubRepository()
            {
                if (gitRepository is null)
                    return null;
                var match = GithubRepositoryRegex.Match(gitRepository);
                if (!match.Success)
                    return null;
                return new GithubRepository(match.Groups[2].Value, match.Groups[3].Value);
            }
#nullable restore
            [JsonExtensionData]
            public IDictionary<string, JToken> AdditionalData { get; set; }
        }
        public string ProjectSlug { get; set; }
        public long BuildId { get; set; }
        public BuildInfoClass BuildInfo { get; set; }
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
        static JsonSerializerSettings serializerSettings = new() { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() };
        public async Task<PublishedVersion[]> GetPublishedVersions(string btcpayVersion, bool includePreRelease)
        {
            var queryString = $"?includePreRelease={includePreRelease}";
            if (btcpayVersion is not null)
                queryString += $"&btcpayVersion={btcpayVersion}&";
            var result = await httpClient.GetStringAsync($"api/v1/plugins{queryString}");
            return JsonConvert.DeserializeObject<PublishedVersion[]>(result, serializerSettings) ?? throw new InvalidOperationException();
        }
    }
}
