using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins
{
    public class PluginService
    {
        private readonly IOptions<DataDirectories> _dataDirectories;
        private readonly IMemoryCache _memoryCache;
        private readonly ISettingsRepository _settingsRepository;
        private readonly BTCPayServerOptions _btcPayServerOptions;
        private readonly HttpClient _githubClient;
        public PluginService(
            ISettingsRepository settingsRepository,
            IEnumerable<IBTCPayServerPlugin> btcPayServerPlugins,
            IHttpClientFactory httpClientFactory, BTCPayServerOptions btcPayServerOptions, 
            IOptions<DataDirectories> dataDirectories, IMemoryCache memoryCache)
        {
            LoadedPlugins = btcPayServerPlugins;
            _githubClient = httpClientFactory.CreateClient();
            _githubClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("btcpayserver", "1"));
            _settingsRepository = settingsRepository;
            _btcPayServerOptions = btcPayServerOptions;
            _dataDirectories = dataDirectories;
            _memoryCache = memoryCache;
        }

        private async Task<string> CallHttpAndCache(string uri)
        {
            var cacheTime = TimeSpan.FromMinutes(30);
            return await _memoryCache.GetOrCreateAsync(nameof(PluginService) + uri, async entry =>
            {
                entry.AbsoluteExpiration = DateTimeOffset.UtcNow + cacheTime;
                return await  _githubClient.GetStringAsync(uri);
            });
        }

        public IEnumerable<IBTCPayServerPlugin> LoadedPlugins { get; }

        public async Task<AvailablePlugin[]> GetRemotePlugins()
        {
            var resp = await CallHttpAndCache($"https://api.github.com/repos/{_btcPayServerOptions.PluginRemote}/git/trees/master?recursive=1");

            var respObj = JObject.Parse(resp)["tree"] as JArray;

            var detectedPlugins = respObj.Where(token => token["path"].ToString().EndsWith(".btcpay"));

            List<Task<AvailablePlugin>> result = new List<Task<AvailablePlugin>>();
            foreach (JToken detectedPlugin in detectedPlugins)
            {
                var pluginName = detectedPlugin["path"].ToString();
                
                var metadata =  respObj.SingleOrDefault(token => (pluginName + ".json")== token["path"].ToString());
                if (metadata is null)
                {
                    continue;
                }
                result.Add( CallHttpAndCache(metadata["url"].ToString())
                    .ContinueWith(
                    task =>
                    {
                        var d = JObject.Parse(task.Result);

                        var content = Encoders.Base64.DecodeData(d["content"].Value<string>());

                        var r = JsonConvert.DeserializeObject<AvailablePlugin>(Encoding.UTF8.GetString(content));
                        r.Path = $"https://raw.githubusercontent.com/{_btcPayServerOptions.PluginRemote}/master/{pluginName}";
                        return r;
                    }, TaskScheduler.Current));
                
            }

            return await Task.WhenAll(result);
        }
        public async Task DownloadRemotePlugin(string plugin, string path)
        {
            var dest = _dataDirectories.Value.PluginDir;
            
            var filedest = Path.Join(dest, plugin);
            Directory.CreateDirectory(Path.GetDirectoryName(filedest));
            using var resp2 = await _githubClient.GetAsync(path); 
            using var fs = new FileStream(filedest, FileMode.Create, FileAccess.ReadWrite);
            await resp2.Content.CopyToAsync(fs);
            await fs.FlushAsync();
        }

        public void InstallPlugin(string plugin)
        {
            var dest = _dataDirectories.Value.PluginDir;
            UninstallPlugin(plugin);
            PluginManager.QueueCommands(dest, ("install", plugin));
        }
        public void UpdatePlugin(string plugin)
        {
            var dest = _dataDirectories.Value.PluginDir;
            PluginManager.QueueCommands(dest, ("update", plugin));
        }

        public async Task UploadPlugin(IFormFile plugin)
        {
            var dest = _dataDirectories.Value.PluginDir;
            var filedest = Path.Combine(dest, plugin.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(filedest));
            if (Path.GetExtension(filedest) == PluginManager.BTCPayPluginSuffix)
            {
                await using var stream = new FileStream(filedest, FileMode.Create);
                await plugin.CopyToAsync(stream);
            }
        }

        public void UninstallPlugin(string plugin)
        {
            var dest = _dataDirectories.Value.PluginDir;
            PluginManager.QueueCommands(dest, ("delete", plugin));
        }

        public class AvailablePlugin : IBTCPayServerPlugin
        {
            public string Identifier { get; set; }
            public string Name { get; set; }
            public Version Version { get; set; }
            public string Description { get; set; }
            public bool SystemPlugin { get; set; } = false;

            public IBTCPayServerPlugin.PluginDependency[] Dependencies { get; set; } = Array.Empty<IBTCPayServerPlugin.PluginDependency>();
            public string Path { get; set; }

            public void Execute(IApplicationBuilder applicationBuilder,
                IServiceProvider applicationBuilderApplicationServices)
            {
            }

            public void Execute(IServiceCollection applicationBuilder)
            {
            }
        }

        class GithubFile
        {
            [JsonProperty("name")] public string Name { get; set; }

            [JsonProperty("sha")] public string Sha { get; set; }
            [JsonProperty("type")] public string Type { get; set; }
            [JsonProperty("path")] public string Path { get; set; }

            [JsonProperty("download_url")] public string DownloadUrl { get; set; }
        }

        public (string command, string plugin)[] GetPendingCommands()
        {
            return PluginManager.GetPendingCommands(_dataDirectories.Value.PluginDir);
        }

        public void CancelCommands(string plugin)
        {
            PluginManager.CancelCommands(_dataDirectories.Value.PluginDir, plugin);
        }

        public string[] GetDisabledPlugins()
        {
            return PluginManager.GetDisabledPlugins(_dataDirectories.Value.PluginDir);
        }
    }
}
