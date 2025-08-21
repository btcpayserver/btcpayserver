using System;
using System.Collections.Concurrent;
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
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<PluginService> _logger;
        private static readonly HashSet<string> _builtInPluginIdentifiers = new(StringComparer.OrdinalIgnoreCase)
        {
            "BTCPayServer",
            "BTCPayServer.Plugins.Altcoins",
            "BTCPayServer.Plugins.Bitcoin",
            "BTCPayServer.Plugins.Crowdfund",
            "BTCPayServer.Plugins.NFC",
            "BTCPayServer.Plugins.PayButton",
            "BTCPayServer.Plugins.PointOfSale",
            "BTCPayServer.Plugins.Shopify"
        };

        public PluginService(
            IEnumerable<IBTCPayServerPlugin> btcPayServerPlugins,
            PluginBuilderClient pluginBuilderClient,
            IOptions<DataDirectories> dataDirectories,
            PoliciesSettings policiesSettings,
            BTCPayServerEnvironment env,
            ILogger<PluginService> logger
            )
        {
            LoadedPlugins = btcPayServerPlugins;
            Installed = btcPayServerPlugins.ToDictionary(p => p.Identifier, p => p.Version, StringComparer.OrdinalIgnoreCase);
            _pluginBuilderClient = pluginBuilderClient;
            _dataDirectories = dataDirectories;
            _policiesSettings = policiesSettings;
            Env = env;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        public async Task<AvailablePlugin[]> GetRemotePlugins(string searchPluginName)
        {
            string btcpayVersion = Env.Version.TrimStart('v').Split('+')[0];
            var versions = await _pluginBuilderClient.GetPublishedVersions(
                btcpayVersion, _policiesSettings.PluginPreReleases, searchPluginName);

            var plugins = versions
                .Select(MapToAvailablePlugin)
                .Where(p => p is not null)
                .ToList()!;

            var unlistedUpdates = await GetUpdatesForUnlistedInstalledAsync(plugins, btcpayVersion);
            plugins.AddRange(unlistedUpdates);

            return plugins.ToArray();
        }

        private async Task<List<AvailablePlugin>> GetUpdatesForUnlistedInstalledAsync(
            List<AvailablePlugin> listedPlugins,
            string btcpayVersion,
            CancellationToken ct = default)
        {
            var listedIdentifiers = new HashSet<string>(
                listedPlugins.Select(p => p.Identifier),
                StringComparer.OrdinalIgnoreCase);

            var installedToCheck = Installed
                .Where(installedPlugin => !_builtInPluginIdentifiers.Contains(installedPlugin.Key) &&
                              !listedIdentifiers.Contains(installedPlugin.Key))
                .ToList();

            var results = new ConcurrentBag<AvailablePlugin>();

            var parallelOpts = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(6, Environment.ProcessorCount),
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(installedToCheck, parallelOpts, async (installedPlugin, ct2) =>
            {
                var (installedIdentifier, installedVersion) = installedPlugin;

                try
                {
                    var publishedVersions = await _pluginBuilderClient.GetPluginVersionsForDownload(
                        installedIdentifier,
                        btcpayVersion,
                        _policiesSettings.PluginPreReleases,
                        includeAllVersions: true);

                    if (publishedVersions == null || !publishedVersions.Any())
                        return;

                    var latestCandidate = publishedVersions
                        .Select(publishedVersion => (VersionInfo: publishedVersion, Manifest: publishedVersion.ManifestInfo))
                        .Where(versionTuple => versionTuple.Manifest != null)
                        .Select(versionTuple =>
                        {
                            var identifier = versionTuple.Manifest!["Identifier"]?.ToString();
                            var parsedVersion = Version.TryParse(versionTuple.Manifest!["Version"]?.ToString(), out var ver) ? ver : null;

                            return new { versionTuple.VersionInfo, Identifier = identifier, Version = parsedVersion };
                        })
                        .Where(candidate => candidate.Identifier != null &&
                                    candidate.Identifier.Equals(installedIdentifier, StringComparison.OrdinalIgnoreCase) &&
                                    candidate.Version is not null)
                        .OrderByDescending(candidate => candidate.Version)
                        .FirstOrDefault();

                    if (latestCandidate == null)
                        return;

                    var latestAvailable = MapToAvailablePlugin(latestCandidate.VersionInfo);

                    if (latestAvailable is null)
                        return;

                    if (latestAvailable.Version <= installedVersion)
                        return;

                    results.Add(latestAvailable);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Error while checking for updates for installed plugin {InstalledPluginIdentifier}",
                        installedIdentifier);
                }
            });

            return results.ToList();
        }

        private AvailablePlugin MapToAvailablePlugin(PublishedVersion publishedVersion)
        {
            var availablePlugin = publishedVersion.ManifestInfo.ToObject<AvailablePlugin>();
            if (availablePlugin is null)
            {
                _logger.LogWarning("ManifestInfo missing for published version {Version}", publishedVersion.ToString());
                return null;
            }

            availablePlugin.Documentation = publishedVersion.Documentation;
            var github = publishedVersion.BuildInfo?.GetGithubRepository();
            if (github != null)
            {
                availablePlugin.Source = github.GetSourceUrl(publishedVersion.BuildInfo.gitCommit, publishedVersion.BuildInfo.pluginDir);
                availablePlugin.Author = github.Owner;
                availablePlugin.AuthorLink = $"https://github.com/{github.Owner}";
            }
            availablePlugin.SystemPlugin = false;
            return availablePlugin;
        }

        public async Task<AvailablePlugin> DownloadRemotePlugin(string pluginIdentifier, string version, VersionCondition condition = null)
        {
            if (version is null)
            {
                string btcpayVersion = Env.Version.TrimStart('v').Split('+')[0];
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
