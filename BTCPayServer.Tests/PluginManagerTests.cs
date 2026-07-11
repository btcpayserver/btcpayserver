using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins;
using BTCPayServer.Plugins.PluginManagement;
using BTCPayServer.Plugins.PluginManagement.Controllers;
using BTCPayServer.Plugins.PluginManagement.Models;
using BTCPayServer.Services;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json;
using Xunit;

namespace BTCPayServer.Tests
{
    [Trait("Fast", "Fast")]
    public class PluginManagerTests : UnitTestBase
    {
        public PluginManagerTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public async Task InstalledPluginsViewModel_ReturnsDisabledPluginUpdateWhenNewerVersionAvailable()
        {
            var model = await CreateInstalledPluginsViewModel(
                disabled: new Dictionary<string, Version> { { "TestPlugin", new Version(1, 0, 0, 0) } },
                allAvailable: [MakeAvailablePlugin("TestPlugin", "1.1.0")]);

            var plugin = Assert.Single(model.DisabledPlugins);
            Assert.Equal(new Version(1, 1, 0), plugin.RecommendedUpdateVersion);
            var updateAction = Assert.Single(plugin.Actions, action => action.FormAction == "InstallPlugin");
            Assert.Equal("1.1.0", updateAction.Version);
        }

        [Fact]
        public async Task InstalledPluginsViewModel_DoesNotAddDisabledPluginUpdateWhenSameVersion()
        {
            var model = await CreateInstalledPluginsViewModel(
                disabled: new Dictionary<string, Version> { { "TestPlugin", new Version(1, 0, 0, 0) } },
                allAvailable: [MakeAvailablePlugin("TestPlugin", "1.0.0")]);

            var plugin = Assert.Single(model.DisabledPlugins);
            Assert.Null(plugin.RecommendedUpdateVersion);
            Assert.DoesNotContain(plugin.Actions, action => action.FormAction == "InstallPlugin");
        }

        [Fact]
        public async Task InstalledPluginsViewModel_DoesNotAddDisabledPluginUpdateWhenNoRemotePlugins()
        {
            var model = await CreateInstalledPluginsViewModel(
                disabled: new Dictionary<string, Version> { { "TestPlugin", new Version(1, 0, 0, 0) } },
                allAvailable: []);

            var plugin = Assert.Single(model.DisabledPlugins);
            Assert.Null(plugin.RecommendedUpdateVersion);
            Assert.DoesNotContain(plugin.Actions, action => action.FormAction == "InstallPlugin");
        }

        [Fact]
        public async Task InstalledPluginsViewModel_SkipsDisabledPluginUpdateWhenVersionIsUnknown()
        {
            var model = await CreateInstalledPluginsViewModel(
                disabled: new Dictionary<string, Version> { { "TestPlugin", null } },
                allAvailable: [MakeAvailablePlugin("TestPlugin", "1.1.0")]);

            var plugin = Assert.Single(model.DisabledPlugins);
            Assert.Null(plugin.RecommendedUpdateVersion);
            Assert.DoesNotContain(plugin.Actions, action => action.FormAction == "InstallPlugin");
        }

        [Fact]
        public async Task InstalledPluginsViewModel_UsesCaseInsensitiveDisabledPluginIdentifierMatching()
        {
            var model = await CreateInstalledPluginsViewModel(
                disabled: new Dictionary<string, Version> { { "MyPlugin", new Version(1, 0, 0, 0) } },
                allAvailable: [MakeAvailablePlugin("myplugin", "1.1.0")]);

            var plugin = Assert.Single(model.DisabledPlugins);
            Assert.Equal(new Version(1, 1, 0), plugin.RecommendedUpdateVersion);
            var updateAction = Assert.Single(plugin.Actions, action => action.FormAction == "InstallPlugin");
            Assert.Equal("1.1.0", updateAction.Version);
        }

        [Fact]
        public async Task InstalledPluginsViewModel_UsesNewestVersionFromMultipleEntries()
        {
            var model = await CreateInstalledPluginsViewModel(
                disabled: new Dictionary<string, Version> { { "TestPlugin", new Version(1, 0, 0, 0) } },
                allAvailable: [
                    MakeAvailablePlugin("TestPlugin", "1.1.0"),
                    MakeAvailablePlugin("TestPlugin", "1.3.0"),
                    MakeAvailablePlugin("TestPlugin", "1.2.0")
                ]);

            var plugin = Assert.Single(model.DisabledPlugins);
            Assert.Equal(new Version(1, 3, 0), plugin.RecommendedUpdateVersion);
            var updateAction = Assert.Single(plugin.Actions, action => action.FormAction == "InstallPlugin");
            Assert.Equal("1.3.0", updateAction.Version);
        }

        [Fact]
        public void PluginDirectoryViewModel_PrefersHighestAvailableVersionWithSatisfiedDependencies()
        {
            var model = CreatePluginDirectoryViewModel(
                selectedSlug: "testplugin",
                allAvailable: [
                    MakeAvailablePlugin("TestPlugin", "2.0.0", ("MissingDependency", ">=1.0.0")),
                    MakeAvailablePlugin("TestPlugin", "1.5.0")
                ]);

            Assert.Equal("1.5.0", model.SelectedPluginPanel.InstallVersion);
        }

        [Fact]
        public void PluginVersionSelection_UsesRequestedCompatibleVersion()
        {
            var selectedVersion = PluginService.SelectCompatiblePluginVersion(
                "TestPlugin",
                "1.5.0",
                null,
                [
                    MakeAvailablePlugin("TestPlugin", "1.5.0"),
                    MakeAvailablePlugin("TestPlugin", "1.4.0")
                ]);

            Assert.Equal(new Version(1, 5, 0), selectedVersion);
        }

        [Fact]
        public void PluginVersionSelection_RejectsRequestedVersionOutsideCompatibleSet()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                PluginService.SelectCompatiblePluginVersion(
                    "TestPlugin",
                    "2.0.0",
                    null,
                    [MakeAvailablePlugin("TestPlugin", "1.5.0")]));

            Assert.Contains("not compatible", ex.Message);
        }

        [Fact]
        public void PluginVersionSelection_DoesNotUseVersionsFromOtherIdentifiers()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                PluginService.SelectCompatiblePluginVersion(
                    "TestPlugin",
                    "2.0.0",
                    null,
                    [
                        MakeAvailablePlugin("OtherPlugin", "2.0.0"),
                        MakeAvailablePlugin("TestPlugin", "1.5.0")
                    ]));

            Assert.Contains("not compatible", ex.Message);
        }

        [Fact]
        public async Task DownloadRemotePlugin_RejectsManifestIdentifierMismatch()
        {
            using var httpClient = new HttpClient(new TestHttpMessageHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/api/v1/plugins/TestPlugin")
                {
                    return TestHttpMessageHandler.JsonResponse("""
                                                               [{
                                                                   "projectSlug": "test-plugin",
                                                                   "buildId": 1,
                                                                   "manifestInfo": {
                                                                       "identifier": "TestPlugin",
                                                                       "name": "Test Plugin",
                                                                       "version": "1.5.0"
                                                                   },
                                                                   "buildInfo": {}
                                                               }]
                                                               """);
                }

                if (path.Contains("/versions/1.5.0", StringComparison.Ordinal) && !path.EndsWith("/download", StringComparison.Ordinal))
                {
                    return TestHttpMessageHandler.JsonResponse("""
                                                               {
                                                                   "projectSlug": "test-plugin",
                                                                   "buildId": 1,
                                                                   "manifestInfo": {
                                                                       "identifier": "OtherPlugin",
                                                                       "name": "Other Plugin",
                                                                       "version": "1.5.0"
                                                                   },
                                                                   "buildInfo": {}
                                                               }
                                                               """);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }));
            httpClient.BaseAddress = new Uri("https://plugins.example/");
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-test-{Guid.NewGuid():N}");
            try
            {
                var pluginService = new PluginService(
                    [],
                    new PluginBuilderClient(httpClient),
                    Options.Create(new DataDirectories { PluginDir = pluginDir }),
                    new PoliciesSettings(),
                    new BTCPayServerEnvironment(null, CreateNetworkProvider(ChainName.Regtest), null, new BTCPayServerOptions()));

                var ex = await Assert.ThrowsAsync<InvalidDataException>(() => pluginService.DownloadRemotePlugin("TestPlugin", "1.5.0"));

                Assert.Contains("does not match requested plugin", ex.Message);
            }
            finally
            {
                if (Directory.Exists(pluginDir))
                    Directory.Delete(pluginDir, true);
            }
        }

        [Fact]
        public async Task InstalledPluginsViewModel_DropsUnsafeMetadataLinks()
        {
            var availablePlugin = MakeAvailablePlugin("TestPlugin", "1.0.0");
            availablePlugin.Author = "Author";
            availablePlugin.AuthorLink = "javascript:alert(1)";
            availablePlugin.Source = "https://github.com/btcpayserver/test-plugin";
            availablePlugin.Documentation = "/relative-docs";

            var model = await CreateInstalledPluginsViewModel(
                loadedPlugins: [MakeLoadedPlugin("TestPlugin")],
                allAvailable: [availablePlugin]);

            var plugin = Assert.Single(model.InstalledPlugins).Current;
            Assert.Equal("Author", plugin.Author);
            Assert.Null(plugin.AuthorLink);
            Assert.Equal("https://github.com/btcpayserver/test-plugin", plugin.Source);
            Assert.Null(plugin.Documentation);
        }

        [Fact]
        public void PluginDirectoryViewModel_IgnoresSelectionWhenSlugIsNotInCatalog()
        {
            var model = CreatePluginDirectoryViewModel(selectedSlug: "unlisted-plugin");

            Assert.Null(model.SelectedPluginPanel.SelectedSlug);
            Assert.Null(model.SelectedPluginPanel.PluginIdentifier);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public async Task SelectedPluginPanel_ReturnsBadRequestWhenSlugIsMissing(string slug)
        {
            using var httpClient = new HttpClient(new TestHttpMessageHandler(_ =>
                TestHttpMessageHandler.JsonResponse("[]")))
            {
                BaseAddress = new Uri("https://plugins.example/")
            };
            var controller = CreatePluginManagerController(Path.GetTempPath(), [], httpClient);

            var result = await controller.SelectedPluginPanel(slug);

            Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>(result);
        }

        [Fact]
        public async Task SelectedPluginPanel_ReturnsServiceUnavailableWhenCatalogLookupFails()
        {
            using var httpClient = new HttpClient(new TestHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
            {
                BaseAddress = new Uri("https://plugins.example/")
            };
            var controller = CreatePluginManagerController(Path.GetTempPath(), [], httpClient);

            var result = await controller.SelectedPluginPanel("testplugin");

            var statusCode = Assert.IsType<Microsoft.AspNetCore.Mvc.StatusCodeResult>(result);
            Assert.Equal((int)HttpStatusCode.ServiceUnavailable, statusCode.StatusCode);
        }

        [Fact]
        public void PluginDirectoryViewModel_HidesInstalledAndDisabledIdentifiersForEmbed()
        {
            var model = CreatePluginDirectoryViewModel(
                disabled: new Dictionary<string, Version> { { "DisabledPlugin", new Version(1, 0, 0) } },
                loadedPlugins: [MakeLoadedPlugin("InstalledPlugin")]);

            Assert.Equal(["DisabledPlugin", "InstalledPlugin"], model.HiddenPluginIdentifiers);
        }

        [Fact]
        public void PluginDirectoryViewModel_DoesNotAllowInstallForInstalledPlugin()
        {
            var model = CreatePluginDirectoryViewModel(
                selectedSlug: "installedplugin",
                loadedPlugins: [MakeLoadedPlugin("InstalledPlugin")],
                allAvailable: [MakeAvailablePlugin("InstalledPlugin", "1.1.0")]);

            Assert.Equal("InstalledPlugin", model.SelectedPluginPanel.PluginIdentifier);
            Assert.Equal("installedplugin", model.SelectedPluginPanel.SelectedSlug);
            Assert.Null(model.SelectedPluginPanel.InstallVersion);
        }

        [Fact]
        public void PluginDirectoryViewModel_DoesNotAllowInstallForDisabledPlugin()
        {
            var model = CreatePluginDirectoryViewModel(
                selectedSlug: "disabledplugin",
                disabled: new Dictionary<string, Version> { { "DisabledPlugin", new Version(1, 0, 0) } },
                allAvailable: [MakeAvailablePlugin("DisabledPlugin", "1.1.0")]);

            Assert.Equal("DisabledPlugin", model.SelectedPluginPanel.PluginIdentifier);
            Assert.Null(model.SelectedPluginPanel.InstallVersion);
        }

        [Fact]
        public void PluginDirectoryViewModel_ExposesPendingAction()
        {
            var pendingManifest = MakeAvailablePlugin("TestPlugin", "0.9.0");
            var model = CreatePluginDirectoryViewModel(
                selectedSlug: "testplugin",
                allAvailable: [MakeAvailablePlugin("TestPlugin", "1.0.0")],
                command: ("install", "TestPlugin"),
                pendingManifest: pendingManifest);

            Assert.True(model.HasPendingActions);
            Assert.Equal("install", model.SelectedPluginPanel.PendingAction);
            Assert.Null(model.SelectedPluginPanel.InstallVersion);
            Assert.Equal(new Version(0, 9, 0), model.SelectedPluginPanel.PendingVersion);
        }

        [Fact]
        public void PluginService_LoadsPendingInstallAndEnableManifests()
        {
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-manifest-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            try
            {
                var installManifest = MakeAvailablePlugin("InstallPlugin", "1.2.0", ("InstallDependency", ">=1.0.0"));
                File.WriteAllText(
                    Path.Combine(pluginDir, "InstallPlugin.json"),
                    JsonConvert.SerializeObject(installManifest));

                var enablePluginDir = Path.Combine(pluginDir, "EnablePlugin");
                Directory.CreateDirectory(enablePluginDir);
                var enableManifest = MakeAvailablePlugin("EnablePlugin", "2.3.0", ("EnableDependency", ">=2.0.0"));
                File.WriteAllText(
                    Path.Combine(enablePluginDir, "EnablePlugin.json"),
                    JsonConvert.SerializeObject(enableManifest));

                using var httpClient = new HttpClient { BaseAddress = new Uri("https://plugins.example/") };
                var pluginService = new PluginService(
                    [],
                    new PluginBuilderClient(httpClient),
                    Options.Create(new DataDirectories { PluginDir = pluginDir }),
                    new PoliciesSettings(),
                    new BTCPayServerEnvironment(null, CreateNetworkProvider(ChainName.Regtest), null, new BTCPayServerOptions()));

                var pendingInstall = pluginService.GetPendingPluginManifest("install", "InstallPlugin");
                Assert.Equal(new Version(1, 2, 0), pendingInstall.Version);
                Assert.Equal("InstallDependency", Assert.Single(pendingInstall.Dependencies).Identifier);

                var pendingEnable = pluginService.GetPendingPluginManifest("enable", "EnablePlugin");
                Assert.Equal(new Version(2, 3, 0), pendingEnable.Version);
                Assert.Equal("EnableDependency", Assert.Single(pendingEnable.Dependencies).Identifier);
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        [Fact]
        public async Task InstalledPluginsViewModel_BlocksUninstallWhenPendingInstallDependsOnInstalledPlugin()
        {
            var model = await CreateInstalledPluginsViewModel(
                loadedPlugins: [MakeLoadedPlugin("Dependency")],
                allAvailable: [MakeAvailablePlugin("Dependent", "1.0.0", ("Dependency", ">=1.0.0"))],
                command: ("install", "Dependent"));

            var plugin = Assert.Single(model.InstalledPlugins);
            var blockedAction = Assert.Single(plugin.Actions);
            Assert.Null(blockedAction.FormAction);
            Assert.NotNull(blockedAction.Tooltip);
        }

        [Fact]
        public async Task InstalledPluginsViewModel_UsesPendingInstallManifestForUninstallProtection()
        {
            var pendingManifest = MakeAvailablePlugin("Dependent", "1.0.0", ("Dependency", ">=1.0.0"));
            var model = await CreateInstalledPluginsViewModel(
                loadedPlugins: [MakeLoadedPlugin("Dependency")],
                allAvailable: [MakeAvailablePlugin("Dependent", "2.0.0")],
                command: ("install", "Dependent"),
                pendingManifest: pendingManifest);

            var plugin = Assert.Single(model.InstalledPlugins);
            var blockedAction = Assert.Single(plugin.Actions);
            Assert.Null(blockedAction.FormAction);
        }

        [Fact]
        public async Task InstalledPluginsViewModel_UsesPendingEnableManifestInsteadOfLatestRemote()
        {
            var pendingManifest = MakeAvailablePlugin("Dependent", "1.0.0");
            var model = await CreateInstalledPluginsViewModel(
                loadedPlugins: [MakeLoadedPlugin("Dependency")],
                allAvailable: [MakeAvailablePlugin("Dependent", "2.0.0", ("Dependency", ">=1.0.0"))],
                command: ("enable", "Dependent"),
                pendingManifest: pendingManifest);

            var plugin = Assert.Single(model.InstalledPlugins);
            Assert.Single(plugin.Actions, action => action.FormAction == "UnInstallPlugin");
        }

        [Fact]
        public async Task InstalledPluginsViewModel_DoesNotBlockUninstallWhenDependentPluginIsPendingDelete()
        {
            var model = await CreateInstalledPluginsViewModel(
                loadedPlugins: [
                    MakeLoadedPlugin("Dependency"),
                    MakeLoadedPlugin("Dependent", ("Dependency", ">=1.0.0"))
                ],
                command: ("delete", "Dependent"));

            var plugin = Assert.Single(model.InstalledPlugins, plugin => plugin.Current.Identifier == "Dependency");
            Assert.Single(plugin.Actions, action => action.FormAction == "UnInstallPlugin");
            Assert.DoesNotContain(plugin.Actions, action => action.FormAction is null);
        }

        [Fact]
        public void PluginDirectoryIframeUrl_IncludesCompatibilityQuery()
        {
            var url = UIPluginManagerController.BuildDirectoryIframeUrl(
                new Uri("https://plugins.example.com/catalog?tenant=one#section"),
                "2.3.7",
                true);

            Assert.Equal(
                "https://plugins.example.com/catalog/public/plugins?embed=1&btcpayVersion=2.3.7&includePreRelease=true",
                url);
        }

        [Fact]
        public async Task PluginBuilderClientConfiguration_PreservesPluginSourceSubpath()
        {
            Uri requestedUri = null;
            using var httpClient = new HttpClient(new TestHttpMessageHandler(request =>
            {
                requestedUri = request.RequestUri;
                return TestHttpMessageHandler.JsonResponse("[]");
            }));
            PluginManagerPlugin.ConfigurePluginBuilderClient(
                new PoliciesSettings { PluginSource = "https://plugins.example.com/catalog?tenant=one#section" },
                httpClient);

            await new PluginBuilderClient(httpClient).GetPublishedVersions("2.3.7", false);

            Assert.Equal("https://plugins.example.com/catalog/", httpClient.BaseAddress.AbsoluteUri);
            Assert.Equal("/catalog/api/v1/plugins", requestedUri.AbsolutePath);
            Assert.Contains("btcpayVersion=2.3.7", requestedUri.Query);
            Assert.DoesNotContain("tenant=one", requestedUri.Query);
            Assert.Empty(requestedUri.Fragment);
        }

        [Theory]
        [InlineData("https://btcpay.example/catalog", "https", "btcpay.example", true)]
        [InlineData("https://btcpay.example:443/catalog?tenant=one#section", "https", "btcpay.example", true)]
        [InlineData("https://btcpay.example:8443/catalog", "https", "btcpay.example:8443", true)]
        [InlineData("https://plugins.btcpay.example/catalog", "https", "btcpay.example", false)]
        [InlineData("https://btcpay.example:8443/catalog", "https", "btcpay.example", false)]
        [InlineData("http://btcpay.example/catalog", "https", "btcpay.example", false)]
        public void PluginEmbed_UsesOpaqueSandboxOnlyForSameOriginSources(
            string pluginSource,
            string requestScheme,
            string requestHost,
            bool expected)
        {
            var result = UIPluginManagerController.ShouldUseOpaqueSandbox(
                new Uri(pluginSource),
                requestScheme,
                new Microsoft.AspNetCore.Http.HostString(requestHost));

            Assert.Equal(expected, result);
        }

        [Fact]
        public void PluginEmbed_DefaultsToOpaqueSandboxWithoutAValidOrigin()
        {
            Assert.True(UIPluginManagerController.ShouldUseOpaqueSandbox(
                null,
                null,
                default));
        }

        private static PluginService.AvailablePlugin MakeAvailablePlugin(
            string identifier, string version, params (string id, string condition)[] dependencies)
        {
            return new PluginService.AvailablePlugin
            {
                Identifier = identifier,
                CatalogSlug = identifier.ToLowerInvariant(),
                Name = identifier,
                Version = Version.Parse(version),
                Dependencies = dependencies.Select(d => new IBTCPayServerPlugin.PluginDependency
                {
                    Identifier = d.id,
                    Condition = d.condition
                }).ToArray()
            };
        }

        private static IBTCPayServerPlugin MakeLoadedPlugin(
            string identifier,
            params (string id, string condition)[] dependencies)
        {
            return new TestPlugin(
                identifier,
                dependencies.Select(d => new IBTCPayServerPlugin.PluginDependency
                {
                    Identifier = d.id,
                    Condition = d.condition
                }).ToArray());
        }

        private async Task<InstalledPluginsViewModel> CreateInstalledPluginsViewModel(
            Dictionary<string, Version> disabled = null,
            IEnumerable<PluginService.AvailablePlugin> allAvailable = null,
            IEnumerable<IBTCPayServerPlugin> loadedPlugins = null,
            (string action, string plugin)? command = null,
            PluginService.AvailablePlugin pendingManifest = null)
        {
            var loaded = loadedPlugins?.ToArray() ?? [];
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-projection-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            try
            {
                WritePluginState(pluginDir, disabled, command, pendingManifest);
                using var httpClient = new HttpClient { BaseAddress = new Uri("https://plugins.example/") };
                var controller = CreatePluginManagerController(pluginDir, loaded, httpClient);
                return await controller.CreateInstalledPluginsViewModel(allAvailable ?? []);
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        private PluginDirectoryViewModel CreatePluginDirectoryViewModel(
            Dictionary<string, Version> disabled = null,
            IEnumerable<PluginService.AvailablePlugin> allAvailable = null,
            string selectedSlug = null,
            IEnumerable<IBTCPayServerPlugin> loadedPlugins = null,
            (string action, string plugin)? command = null,
            PluginService.AvailablePlugin pendingManifest = null)
        {
            var loaded = loadedPlugins?.ToArray() ?? [];
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-directory-projection-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            try
            {
                WritePluginState(pluginDir, disabled, command, pendingManifest);
                using var httpClient = new HttpClient { BaseAddress = new Uri("https://plugins.example/") };
                var controller = CreatePluginManagerController(pluginDir, loaded, httpClient);
                return controller.CreatePluginDirectoryViewModel(selectedSlug, allAvailable?.ToArray() ?? []);
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        private UIPluginManagerController CreatePluginManagerController(
            string pluginDir,
            IEnumerable<IBTCPayServerPlugin> loadedPlugins,
            HttpClient httpClient)
        {
            var policiesSettings = new PoliciesSettings();
            var pluginService = new PluginService(
                loadedPlugins,
                new PluginBuilderClient(httpClient),
                Options.Create(new DataDirectories { PluginDir = pluginDir }),
                policiesSettings,
                new BTCPayServerEnvironment(null, CreateNetworkProvider(ChainName.Regtest), null, new BTCPayServerOptions()));
            return new UIPluginManagerController(pluginService, policiesSettings, null);
        }

        private static void WritePluginState(
            string pluginDir,
            Dictionary<string, Version> disabled,
            (string action, string plugin)? command,
            PluginService.AvailablePlugin pendingManifest)
        {
            if (disabled is { Count: > 0 })
            {
                File.WriteAllLines(Path.Combine(pluginDir, "disabled"), disabled.Keys);
                foreach (var (identifier, version) in disabled)
                {
                    var disabledPluginDir = Path.Combine(pluginDir, identifier);
                    Directory.CreateDirectory(disabledPluginDir);
                    if (version is null)
                        continue;

                    var manifest = MakeAvailablePlugin(identifier, version.ToString());
                    File.WriteAllText(
                        Path.Combine(disabledPluginDir, identifier + ".json"),
                        JsonConvert.SerializeObject(manifest));
                }
            }

            if (command is not { } pendingCommand)
                return;

            var (action, plugin) = pendingCommand;
            File.WriteAllText(Path.Combine(pluginDir, "commands"), $"{action}:{plugin}");
            var manifestPath = action switch
            {
                "install" => Path.Combine(pluginDir, plugin + ".json"),
                "enable" => Path.Combine(pluginDir, plugin, plugin + ".json"),
                _ => null
            };
            if (pendingManifest is null || manifestPath is null)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(pendingManifest));
        }

        private sealed class TestPlugin(
            string identifier,
            IBTCPayServerPlugin.PluginDependency[] dependencies) : BaseBTCPayServerPlugin
        {
            public override string Identifier => identifier;
            public override string Name => identifier;
            public override Version Version => new(1, 0, 0);
            public override string Description => identifier;
            public override IBTCPayServerPlugin.PluginDependency[] Dependencies => dependencies;
        }
    }
}
