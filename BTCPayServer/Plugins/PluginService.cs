using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins
{
    public class PluginService
    {
        private readonly IOptions<DataDirectories> _dataDirectories;
        private readonly BTCPayServerOptions _btcPayServerOptions;
        private readonly HttpClient _githubClient;
        public PluginService(IEnumerable<IBTCPayServerPlugin> btcPayServerPlugins,
            IHttpClientFactory httpClientFactory, BTCPayServerOptions btcPayServerOptions, IOptions<DataDirectories> dataDirectories)
        {
            LoadedPlugins = btcPayServerPlugins;
            _githubClient = httpClientFactory.CreateClient();
            _githubClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("btcpayserver", "1"));
            _btcPayServerOptions = btcPayServerOptions;
            _dataDirectories = dataDirectories;
        }

        public IEnumerable<IBTCPayServerPlugin> LoadedPlugins { get; }

        public async Task<IEnumerable<AvailablePlugin>> GetRemotePlugins()
        {
            var resp = await _githubClient
                .GetStringAsync(new Uri($"https://api.github.com/repos/{_btcPayServerOptions.PluginRemote}/contents"));
            var files = JsonConvert.DeserializeObject<GithubFile[]>(resp);
            return await Task.WhenAll(files.Where(file => file.Name.EndsWith($"{PluginManager.BTCPayPluginSuffix}.json", StringComparison.InvariantCulture)).Select(async file =>
            {
                return await _githubClient.GetStringAsync(file.DownloadUrl).ContinueWith(
                    task => JsonConvert.DeserializeObject<AvailablePlugin>(task.Result), TaskScheduler.Current);
            }));
        }

        public async Task DownloadRemotePlugin(string plugin)
        {
            var dest = _dataDirectories.Value.PluginDir;
            var resp = await _githubClient
                .GetStringAsync(new Uri($"https://api.github.com/repos/{_btcPayServerOptions.PluginRemote}/contents"));
            var files = JsonConvert.DeserializeObject<GithubFile[]>(resp);
            var ext = files.SingleOrDefault(file => file.Name == $"{plugin}{PluginManager.BTCPayPluginSuffix}");
            if (ext is null)
            {
                throw new Exception("Plugin not found on remote");
            }

            if (!ext.Name.IsValidFileName())
            {
                throw new Exception("Invalid file name");
            }
            var filedest = Path.Join(dest, ext.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(filedest));
            new WebClient().DownloadFile(new Uri(ext.DownloadUrl), filedest);
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

            [JsonProperty("download_url")] public string DownloadUrl { get; set; }
        }

        public (string command, string plugin)[] GetPendingCommands()
        {
            return PluginManager.GetPendingCommands(_dataDirectories.Value.PluginDir);
        }

        public  void CancelCommands(string plugin)
        {
            PluginManager.CancelCommands(_dataDirectories.Value.PluginDir, plugin);
        }

        public string[] GetDisabledPlugins()
        {
            return PluginManager.GetDisabledPlugins(_dataDirectories.Value.PluginDir);
        }
    }
}
