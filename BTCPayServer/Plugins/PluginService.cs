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
using Newtonsoft.Json;

namespace BTCPayServer.Plugins
{
    public class PluginService: IPluginHookService
    {
        private readonly DataDirectories _datadirs;
        private readonly BTCPayServerOptions _options;
        private readonly HttpClient _githubClient;
        private readonly IEnumerable<IPluginHookAction> _actions;
        private readonly IEnumerable<IPluginHookFilter> _filters;
        public PluginService(IEnumerable<IBTCPayServerPlugin> btcPayServerPlugins,
            IHttpClientFactory httpClientFactory, DataDirectories datadirs, BTCPayServerOptions options, IEnumerable<IPluginHookAction> actions, IEnumerable<IPluginHookFilter> filters)
        {
            LoadedPlugins = btcPayServerPlugins;
            _githubClient = httpClientFactory.CreateClient();
            _githubClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("btcpayserver", "1"));
            _datadirs = datadirs;
            _options = options;
            _actions = actions;
            _filters = filters;
        }

        public IEnumerable<IBTCPayServerPlugin> LoadedPlugins { get; }

        public async Task<IEnumerable<AvailablePlugin>> GetRemotePlugins()
        {
            var resp = await _githubClient
                .GetStringAsync(new Uri($"https://api.github.com/repos/{_options.PluginRemote}/contents"));
            var files = JsonConvert.DeserializeObject<GithubFile[]>(resp);
            return await Task.WhenAll(files.Where(file => file.Name.EndsWith($"{PluginManager.BTCPayPluginSuffix}.json", StringComparison.InvariantCulture)).Select(async file =>
            {
                return await _githubClient.GetStringAsync(file.DownloadUrl).ContinueWith(
                    task => JsonConvert.DeserializeObject<AvailablePlugin>(task.Result), TaskScheduler.Current);
            }));
        }

        public async Task DownloadRemotePlugin(string plugin)
        {
            var dest = _datadirs.PluginDir;
            var resp = await _githubClient
                .GetStringAsync(new Uri($"https://api.github.com/repos/{_options.PluginRemote}/contents"));
            var files = JsonConvert.DeserializeObject<GithubFile[]>(resp);
            var ext = files.SingleOrDefault(file => file.Name == $"{plugin}{PluginManager.BTCPayPluginSuffix}");
            if (ext is null)
            {
                throw new Exception("Plugin not found on remote");
            }

            var filedest = Path.Combine(dest, ext.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(filedest));
            new WebClient().DownloadFile(new Uri(ext.DownloadUrl), filedest);
        }

        public void InstallPlugin(string plugin)
        {
            var dest = _datadirs.PluginDir;
            UninstallPlugin(plugin);
            PluginManager.QueueCommands(dest, ("install", plugin));
        }
        public void UpdatePlugin(string plugin)
        {
            var dest = _datadirs.PluginDir;
            PluginManager.QueueCommands(dest, ("update", plugin));
        }

        public async Task UploadPlugin(IFormFile plugin)
        {
            var dest = _datadirs.PluginDir;
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
            var dest = _datadirs.PluginDir;
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
            return PluginManager.GetPendingCommands(_datadirs.PluginDir);
        }

        public  void CancelCommands(string plugin)
        {
            PluginManager.CancelCommands(_datadirs.PluginDir, plugin);
        }
        
        public async Task ApplyAction(string hook, object args)
        {
            var filters = _actions
                .Where(filter => filter.Hook.Equals(hook, StringComparison.InvariantCultureIgnoreCase)).ToList();
            foreach (IPluginHookAction pluginHookFilter in filters)
            {
                await pluginHookFilter.Execute(args);
            }
        }

        public async Task<object> ApplyFilter(string hook, object args)
        {
            var filters = _filters
                .Where(filter => filter.Hook.Equals(hook, StringComparison.InvariantCultureIgnoreCase)).ToList();
            foreach (IPluginHookFilter pluginHookFilter in filters)
            {
                args = await pluginHookFilter.Execute(args);
            }

            return args;
        }
    }
}
