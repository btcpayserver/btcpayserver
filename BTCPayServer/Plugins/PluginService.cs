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

        public string GetShortBtcpayVersion() => Env.Version.TrimStart('v').Split('+')[0];
        public Uri GetPluginSourceBaseUri() => _pluginBuilderClient.HttpClient.BaseAddress;
        public bool PluginPreReleasesEnabled => _policiesSettings.PluginPreReleases;

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

            availablePlugin.CatalogSlug = publishedVersion.ProjectSlug;
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
            string btcpayVersion = GetShortBtcpayVersion();
            var versions = await _pluginBuilderClient.GetPluginVersionsForDownload(pluginIdentifier,
                btcpayVersion, _policiesSettings.PluginPreReleases, includeAllVersions: true);
            var compatiblePlugins = versions
                .Select(v => v.ManifestInfo?.ToObject<AvailablePlugin>())
                .Where(v => v is not null)
                .ToArray();
            var selectedVersion = SelectCompatiblePluginVersion(pluginIdentifier, version, condition, compatiblePlugins);

            var dest = _dataDirectories.Value.PluginDir;
            var filedest = Path.Join(dest, pluginIdentifier + ".btcpay");
            var filemanifestdest = Path.Join(dest, pluginIdentifier + ".json");
            var pluginSelector = $"[{Uri.EscapeDataString(pluginIdentifier)}]";
            version = Uri.EscapeDataString(selectedVersion.ToString());
            Directory.CreateDirectory(Path.GetDirectoryName(filedest));
            var url = $"api/v1/plugins/{pluginSelector}/versions/{version}/download";
            var publishedVersion = await _pluginBuilderClient.GetPlugin(pluginSelector, version);
            if (publishedVersion is null)
                throw new InvalidDataException($"Plugin version not found for {pluginIdentifier} {version}.");
            if (publishedVersion.ManifestInfo is null)
                throw new InvalidDataException($"Plugin manifest not found for {pluginIdentifier} {version}.");
            var manifest = publishedVersion.ManifestInfo.ToObject<AvailablePlugin>() ??
                           throw new InvalidDataException($"Plugin manifest deserialized to null for {pluginIdentifier} {version}.");
            if (!string.Equals(manifest.Identifier, pluginIdentifier, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Plugin manifest identifier {manifest.Identifier} does not match requested plugin {pluginIdentifier}.");
            if (!selectedVersion.Equals(manifest.Version))
                throw new InvalidDataException($"Plugin manifest version {manifest.Version} does not match requested version {selectedVersion}.");
            manifest.CatalogSlug = publishedVersion.ProjectSlug;
            using var resp2 = await _pluginBuilderClient.HttpClient.GetAsync(url);
            resp2.EnsureSuccessStatusCode();
            await using var fs = new FileStream(filedest, FileMode.Create, FileAccess.ReadWrite);
            await resp2.Content.CopyToAsync(fs);
            await fs.FlushAsync();
            await File.WriteAllTextAsync(filemanifestdest, JsonConvert.SerializeObject(manifest, Formatting.Indented));
            return manifest;
        }

        internal static Version SelectCompatiblePluginVersion(
            string pluginIdentifier,
            string version,
            VersionCondition condition,
            IEnumerable<AvailablePlugin> compatiblePlugins)
        {
            var potentialVersions = compatiblePlugins
                .Where(v => v is not null)
                .Where(v => string.Equals(v.Identifier, pluginIdentifier, StringComparison.OrdinalIgnoreCase))
                .Where(v => v.Version is not null)
                .Select(v => v.Version)
                .Distinct()
                .ToList();
            if (potentialVersions.Count == 0)
            {
                throw new InvalidOperationException($"Plugin {pluginIdentifier} not found for this BTCPay Server version.");
            }

            if (version is not null)
            {
                if (!Version.TryParse(version, out var requestedVersion))
                    throw new InvalidOperationException($"Plugin {pluginIdentifier} version {version} is invalid.");
                if (!potentialVersions.Contains(requestedVersion))
                    throw new InvalidOperationException($"Plugin {pluginIdentifier} version {version} is not compatible with this BTCPay Server.");
                if (condition is not null && !condition.IsFulfilled(requestedVersion))
                    throw new InvalidOperationException($"Plugin {pluginIdentifier} version {version} does not satisfy condition {condition}.");
                return requestedVersion;
            }

            var selectedVersion = condition is null
                ? potentialVersions.OrderDescending().First()
                : potentialVersions.OrderDescending().FirstOrDefault(condition.IsFulfilled);
            return selectedVersion ?? throw new InvalidOperationException($"No version of plugin {pluginIdentifier} can satisfy condition {condition}");
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
            public string CatalogSlug { get; set; }
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
