using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

    public record InstalledPluginRequest(string Identifier, string Version);

    public class PluginBuilderClient
    {
        private readonly HttpClient _httpClient;
        public HttpClient HttpClient => _httpClient;
        private readonly ILogger<PluginBuilderClient> _logger;
        public PluginBuilderClient(HttpClient httpClient, ILogger<PluginBuilderClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }
        static JsonSerializerSettings serializerSettings = new() { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() };
        public async Task<PublishedVersion[]> GetPublishedVersions(string btcpayVersion, bool includePreRelease, string searchPluginName = null, bool? includeAllVersions = null)
        {
            var queryString = $"?includePreRelease={includePreRelease}";
            if (btcpayVersion is not null)
                queryString += $"&btcpayVersion={btcpayVersion}";
            if (searchPluginName is not null)
                queryString += $"&searchPluginName={searchPluginName}";
            if (includeAllVersions is not null)
                queryString += $"&includeAllVersions={includeAllVersions}";
            var result = await _httpClient.GetStringAsync($"api/v1/plugins{queryString}");
            return JsonConvert.DeserializeObject<PublishedVersion[]>(result, serializerSettings) ?? throw new InvalidOperationException();
        }
        public async Task<PublishedVersion> GetPlugin(string pluginSlug, string version)
        {
            try
            {
                var result = await _httpClient.GetStringAsync($"api/v1/plugins/{pluginSlug}/versions/{version}");
                return JsonConvert.DeserializeObject<PublishedVersion>(result, serializerSettings);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<PublishedVersion[]> GetPluginVersionsForDownload(string identifier, string btcpayVersion, bool includePreRelease = false, bool includeAllVersions = false)
        {
            var queryString = $"?btcpayVersion={btcpayVersion}&includePreRelease={includePreRelease}&includeAllVersions={includeAllVersions}";
            var url = $"api/v1/plugins/{identifier}{queryString}";

            var result = await _httpClient.GetStringAsync(url);
            return JsonConvert.DeserializeObject<PublishedVersion[]>(result, serializerSettings)
                   ?? throw new InvalidOperationException();
        }

        public async Task<PublishedVersion[]> GetInstalledPluginsUpdates(
            string btcpayVersion,
            bool includePreRelease,
            IEnumerable<InstalledPluginRequest> plugins)
        {
            var queryString = $"?includePreRelease={includePreRelease}";
            if (!string.IsNullOrWhiteSpace(btcpayVersion))
                queryString += $"&btcpayVersion={Uri.EscapeDataString(btcpayVersion)}";

            var json = JsonConvert.SerializeObject(plugins, serializerSettings);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using var resp = await _httpClient.PostAsync($"api/v1/plugins/updates{queryString}", content);
                resp.EnsureSuccessStatusCode();

                var body = await resp.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<PublishedVersion[]>(body, serializerSettings)
                       ?? Array.Empty<PublishedVersion>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to check for plugins updates");
                return Array.Empty<PublishedVersion>();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse plugins updates response");
                return Array.Empty<PublishedVersion>();
            }
        }
    }
}
