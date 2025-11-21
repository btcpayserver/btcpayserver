using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins
{
    public class PluginService
    {
        private readonly IOptions<DataDirectories> _dataDirectories;
        private readonly PoliciesSettings _policiesSettings;
        private readonly PluginBuilderClient _pluginBuilderClient;

        public PluginService(
            IEnumerable<IBTCPayServerPlugin> btcPayServerPlugins,
            PluginBuilderClient pluginBuilderClient,
            IOptions<DataDirectories> dataDirectories,
            PoliciesSettings policiesSettings,
            BTCPayServerEnvironment env
            )
        {
            LoadedPlugins = btcPayServerPlugins;
            Installed = btcPayServerPlugins.ToDictionary(p => p.Identifier, p => p.Version, StringComparer.OrdinalIgnoreCase);
            _pluginBuilderClient = pluginBuilderClient;
            _dataDirectories = dataDirectories;
            _policiesSettings = policiesSettings;
            Env = env;
        }

        public Dictionary<string, Version> Installed { get; set; }

        public IEnumerable<IBTCPayServerPlugin> LoadedPlugins { get; }
        public BTCPayServerEnvironment Env { get; }

        public Version GetVersionOfPendingInstall(string plugin)
        {
            var dirName = Path.Combine(_dataDirectories.Value.PluginDir, plugin);
            var manifestFileName = dirName + ".json";
            if (!File.Exists(manifestFileName)) return null;
            var pluginManifest =  JObject.Parse(File.ReadAllText(manifestFileName)).ToObject<AvailablePlugin>();
            return pluginManifest.Version;
        }

        private string GetShortBtcpayVersion() => Env.Version.TrimStart('v').Split('+')[0];

        public async Task<AvailablePlugin[]> GetRemotePlugins(string searchPluginName, CancellationToken cancellationToken = default)
        {
            string btcpayVersion = GetShortBtcpayVersion();
            var versions = await _pluginBuilderClient.GetPublishedVersions(
                btcpayVersion, _policiesSettings.PluginPreReleases, searchPluginName, cancellationToken: cancellationToken);

            var plugins = versions
                .Select(MapToAvailablePlugin)
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();

            var listedIds = new HashSet<string>(
                plugins.Select(p => p.Identifier),
                StringComparer.OrdinalIgnoreCase);

            var loadedToCheck = LoadedPlugins
                .Where(p => !p.SystemPlugin && !listedIds.Contains(p.Identifier))
                .Select(p => new InstalledPluginRequest(p.Identifier, p.Version.ToString()))
                .ToList();

            if (loadedToCheck.Count <= 0) return plugins.ToArray();

            var updates = await _pluginBuilderClient.GetInstalledPluginsUpdates(
                btcpayVersion,
                _policiesSettings.PluginPreReleases,
                loadedToCheck, cancellationToken: cancellationToken);

            if (updates is { Length: > 0 })
            {
                plugins.AddRange(
                    updates.Select(MapToAvailablePlugin)
                        .Where(p => p is not null)
                        .Select(p => p!)
                );
            }

            return plugins.ToArray();
        }

#nullable enable
        private AvailablePlugin? MapToAvailablePlugin(PublishedVersion publishedVersion)
        {
            if (publishedVersion.ManifestInfo is null)
                return null;

            var availablePlugin = publishedVersion.ManifestInfo.ToObject<AvailablePlugin>();
            if (availablePlugin is null)
                throw new InvalidDataException($"Manifest deserialized to null BuildId: {publishedVersion.BuildId} PluginSlug: {publishedVersion.ProjectSlug}");

            availablePlugin.Documentation = publishedVersion.Documentation;
            var buildInfo = publishedVersion.BuildInfo;
            var github = buildInfo?.GetGithubRepository();
            if (buildInfo is not null && github is not null)
            {
                availablePlugin.Source = github.GetSourceUrl(buildInfo.gitCommit, buildInfo.pluginDir);
                availablePlugin.Author = github.Owner;
                availablePlugin.AuthorLink = $"https://github.com/{github.Owner}";
            }
            availablePlugin.SystemPlugin = false;
            return availablePlugin;
        }
#nullable restore

        public async Task<AvailablePlugin> DownloadRemotePlugin(string pluginIdentifier, string version, VersionCondition condition = null)
        {
            if (version is null)
            {
                string btcpayVersion = GetShortBtcpayVersion();
                var versions = await _pluginBuilderClient.GetPluginVersionsForDownload(pluginIdentifier,
                    btcpayVersion, _policiesSettings.PluginPreReleases, includeAllVersions: true);
                var potentialVersions = versions
                    .Select(v => v.ManifestInfo?.ToObject<AvailablePlugin>())
                    .Where(v => v is not null)
                    .Where(v => v.Identifier == pluginIdentifier)
                    .Select(v => v.Version)
                    .ToList();
                if (potentialVersions.Count == 0)
                {
                    throw new InvalidOperationException($"Plugin {pluginIdentifier} not found");
                }

                if (condition is not null)
                {
                    version = potentialVersions
                        .OrderDescending()
                        .FirstOrDefault(condition.IsFulfilled)?.ToString();
                    if (version is null)
                    {
                        throw new InvalidOperationException($"No version of plugin {pluginIdentifier} can satisfy condition {condition}");
                    }
                }
            }

            var dest = _dataDirectories.Value.PluginDir;
            var filedest = Path.Join(dest, pluginIdentifier + ".btcpay");
            var filemanifestdest = Path.Join(dest, pluginIdentifier + ".json");
            var pluginSelector = $"[{Uri.EscapeDataString(pluginIdentifier)}]";
            version = Uri.EscapeDataString(version);
            Directory.CreateDirectory(Path.GetDirectoryName(filedest));
            var url = $"api/v1/plugins/{pluginSelector}/versions/{version}/download";
            var manifest = (await _pluginBuilderClient.GetPlugin(pluginSelector, version))?.ManifestInfo?.ToObject<AvailablePlugin>();
            await File.WriteAllTextAsync(filemanifestdest, JsonConvert.SerializeObject(manifest, Formatting.Indented));
            using var resp2 = await _pluginBuilderClient.HttpClient.GetAsync(url);
            await using var fs = new FileStream(filedest, FileMode.Create, FileAccess.ReadWrite);
            await resp2.Content.CopyToAsync(fs);
            await fs.FlushAsync();
            return manifest;
        }

        public void InstallPlugin(string plugin)
        {
            PluginManager.QueueCommands(_dataDirectories.Value.PluginDir, ("install", plugin));
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
            PluginManager.CancelCommands(dest, plugin);
            PluginManager.QueueCommands(dest, ("delete", plugin));
        }

        public void EnablePlugin(string plugin){
            PluginManager.QueueCommands(_dataDirectories.Value.PluginDir, ("enable", plugin));
        }

        public class AvailablePlugin
        {
            public string Identifier { get; set; }
            public string Name { get; set; }
            public Version Version { get; set; }
            public string Description { get; set; }
            public bool SystemPlugin { get; set; } = false;

            public IBTCPayServerPlugin.PluginDependency[] Dependencies { get; set; } = Array.Empty<IBTCPayServerPlugin.PluginDependency>();
            public string Documentation { get; set; }
            public string Source { get; set; }
            public string Author { get; set; }
            public string AuthorLink { get; set; }

            public void Execute(IApplicationBuilder applicationBuilder, IServiceProvider applicationBuilderApplicationServices)
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

        public Dictionary<string, Version> GetDisabledPlugins()
        {
            return PluginManager.GetDisabledPlugins(_dataDirectories.Value.PluginDir);
        }
    }
}
