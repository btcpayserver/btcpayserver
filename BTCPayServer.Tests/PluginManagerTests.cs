using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins;
using BTCPayServer.Plugins.PluginManagement;
using BTCPayServer.Plugins.PluginManagement.Controllers;
using BTCPayServer.Plugins.PluginManagement.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
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
        public async Task InstalledPluginsViewModel_UsesNewestVersionWithCaseInsensitiveIdentifierMatching()
        {
            var model = await CreateInstalledPluginsViewModel(
                disabled: new Dictionary<string, Version> { { "TestPlugin", new Version(1, 0, 0, 0) } },
                allAvailable: [
                    MakeAvailablePlugin("TestPlugin", "1.1.0"),
                    MakeAvailablePlugin("testplugin", "1.3.0"),
                    MakeAvailablePlugin("TestPlugin", "1.2.0")
                ]);

            var plugin = Assert.Single(model.DisabledPlugins);
            Assert.Equal(new Version(1, 3, 0), plugin.RecommendedUpdateVersion);
            var updateAction = Assert.Single(plugin.Actions, action => action.FormAction == "InstallPlugin");
            Assert.Equal("1.3.0", updateAction.Version);
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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DownloadPluginAndDependencies_LeavesPendingInstallIntactWhenDependenciesAreCyclic(bool dependencyInstalled)
        {
            using var httpClient = CreatePluginCatalogClient(
                MakeAvailablePlugin("PluginA", "1.0.0", ("pluginb", ">=1.0.0")),
                MakeAvailablePlugin("PluginB", "1.0.0", ("plugina", ">=1.0.0")));
            IBTCPayServerPlugin[] loaded = dependencyInstalled
                ? [MakeLoadedPlugin("PluginB", ("PluginA", ">=1.0.0"))]
                : [];
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-cycle-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            try
            {
                var pendingArchive = Path.Combine(pluginDir, "PluginA.btcpay");
                byte[] originalContents = [0x00, 0x01];
                await File.WriteAllBytesAsync(pendingArchive, originalContents);
                PluginManager.QueueCommands(pluginDir, ("install", "PluginA"));
                var controller = CreatePluginManagerController(pluginDir, loaded, httpClient);

                var error = await controller.DownloadPluginAndDependencies("PluginA", null);

                Assert.Contains("cycl", error, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(("install", "PluginA"), Assert.Single(PluginManager.GetPendingCommands(pluginDir)));
                Assert.Equal(originalContents, await File.ReadAllBytesAsync(pendingArchive));
                Assert.False(File.Exists(Path.Combine(pluginDir, "PluginB.btcpay")));
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        [Fact]
        public async Task DownloadPluginAndDependencies_SchedulesSharedDependencyOnlyOnce()
        {
            using var httpClient = CreatePluginCatalogClient(
                MakeAvailablePlugin("PluginA", "1.0.0", ("PluginB", ">=1.0.0"), ("PluginC", ">=1.0.0")),
                MakeAvailablePlugin("PluginB", "1.0.0", ("plugind", ">=1.0.0")),
                MakeAvailablePlugin("PluginC", "1.0.0", ("PluginD", ">=1.0.0")),
                MakeAvailablePlugin("PluginD", "1.0.0"));
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-dependencies-test-{Guid.NewGuid():N}");
            try
            {
                var controller = CreatePluginManagerController(pluginDir, [], httpClient);

                var error = await controller.DownloadPluginAndDependencies("PluginA", null);

                Assert.Null(error);
                Assert.Equal(
                    new[] { "PluginD", "PluginB", "PluginC", "PluginA" },
                    PluginManager.GetPendingCommands(pluginDir).Select(command => command.plugin));
                Assert.True(File.Exists(Path.Combine(pluginDir, "PluginD.btcpay")));
            }
            finally
            {
                if (Directory.Exists(pluginDir))
                    Directory.Delete(pluginDir, true);
            }
        }

        private static HttpClient CreatePluginCatalogClient(params PluginService.AvailablePlugin[] plugins)
        {
            var catalog = plugins.ToDictionary(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase);
            return new HttpClient(new TestHttpMessageHandler(request =>
            {
                var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
                var identifier = path.Split('/')[4].Trim('[', ']');
                if (!catalog.TryGetValue(identifier, out var plugin))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (path.EndsWith("/download", StringComparison.Ordinal))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x50, 0x4b]) };

                var manifest = JsonConvert.SerializeObject(new
                {
                    projectSlug = plugin.CatalogSlug,
                    buildId = 1,
                    manifestInfo = plugin,
                    buildInfo = new { }
                });
                return TestHttpMessageHandler.JsonResponse(
                    path.Contains("/versions/", StringComparison.Ordinal) ? manifest : $"[{manifest}]");
            })) { BaseAddress = new Uri("https://plugins.example/") };
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
        public async Task SelectedPluginPanel_ReturnsServiceUnavailableWhenDirectoryLookupFails()
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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SelectedPluginPanel_LooksUpDirectoryPluginBySlug(bool includePreRelease)
        {
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-directory-selection-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            Uri requestedUri = null;
            using var httpClient = new HttpClient(new TestHttpMessageHandler(request =>
            {
                requestedUri = request.RequestUri;
                return CreatePublishedVersionResponse("barebitcoin", "BTCPayServer.Plugins.BareBitcoin", "Bare Bitcoin", "2.0.1.0");
            }));
            httpClient.BaseAddress = new Uri("https://plugins.example/");
            var controller = CreatePluginManagerController(
                pluginDir,
                [],
                httpClient,
                new PoliciesSettings { PluginPreReleases = includePreRelease });

            try
            {
                var result = await controller.SelectedPluginPanel("barebitcoin");

                var partial = Assert.IsType<Microsoft.AspNetCore.Mvc.PartialViewResult>(result);
                var model = Assert.IsType<PluginSelectedPanelViewModel>(partial.Model);
                Assert.Equal("barebitcoin", model.SelectedSlug);
                Assert.Equal("BTCPayServer.Plugins.BareBitcoin", model.PluginIdentifier);
                Assert.Equal("2.0.1.0", model.InstallVersion);
                Assert.NotNull(requestedUri);
                Assert.Equal("/api/v1/plugins/directory/barebitcoin", requestedUri.AbsolutePath);
                var query = QueryHelpers.ParseQuery(requestedUri.Query);
                Assert.Equal(
                    BTCPayServerEnvironment.GetInformationalVersion().TrimStart('v').Split('+')[0],
                    query["btcpayVersion"].ToString());
                Assert.Equal(includePreRelease, bool.Parse(query["includePreRelease"].ToString()));
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        [Fact]
        public async Task PluginDirectory_LoadsSelectedPluginBySlug()
        {
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-directory-deep-link-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            using var httpClient = new HttpClient(new TestHttpMessageHandler(_ =>
                CreatePublishedVersionResponse("barebitcoin", "BTCPayServer.Plugins.BareBitcoin", "Bare Bitcoin", "2.0.1.0")));
            httpClient.BaseAddress = new Uri("https://plugins.example/");
            var controller = CreatePluginManagerController(pluginDir, [], httpClient);

            try
            {
                var result = await controller.PluginDirectory("barebitcoin");

                var view = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(result);
                var model = Assert.IsType<PluginDirectoryViewModel>(view.Model);
                Assert.Equal("barebitcoin", model.SelectedPluginPanel.SelectedSlug);
                Assert.Equal("BTCPayServer.Plugins.BareBitcoin", model.SelectedPluginPanel.PluginIdentifier);
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        [Theory]
        [InlineData("exolix-malicious", "ExolixMalicious", "1.0.0")]
        [InlineData("exolix", null, "1.0.0")]
        [InlineData("exolix", "Exolix", null)]
        public async Task SelectedPluginPanel_ReturnsServiceUnavailableWhenDirectoryResponseIsInvalid(
            string responseSlug,
            string responseIdentifier,
            string responseVersion)
        {
            using var httpClient = new HttpClient(new TestHttpMessageHandler(_ =>
                CreatePublishedVersionResponse(responseSlug, responseIdentifier, "Exolix", responseVersion)));
            httpClient.BaseAddress = new Uri("https://plugins.example/");
            var controller = CreatePluginManagerController(Path.GetTempPath(), [], httpClient);

            var result = await controller.SelectedPluginPanel("exolix");

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
                loadedPlugins: [MakeLoadedPlugin("InstalledPlugin")],
                selectedPlugin: MakeAvailablePlugin("InstalledPlugin", "1.1.0"));

            Assert.Equal("InstalledPlugin", model.SelectedPluginPanel.PluginIdentifier);
            Assert.Equal("installedplugin", model.SelectedPluginPanel.SelectedSlug);
            Assert.Null(model.SelectedPluginPanel.InstallVersion);
        }

        [Fact]
        public void PluginDirectoryViewModel_DoesNotAllowInstallForDisabledPlugin()
        {
            var model = CreatePluginDirectoryViewModel(
                disabled: new Dictionary<string, Version> { { "DisabledPlugin", new Version(1, 0, 0) } },
                selectedPlugin: MakeAvailablePlugin("DisabledPlugin", "1.1.0"));

            Assert.Equal("DisabledPlugin", model.SelectedPluginPanel.PluginIdentifier);
            Assert.Null(model.SelectedPluginPanel.InstallVersion);
        }

        [Fact]
        public void PluginDirectoryViewModel_ExposesPendingAction()
        {
            var pendingManifest = MakeAvailablePlugin("TestPlugin", "0.9.0");
            var model = CreatePluginDirectoryViewModel(
                selectedPlugin: MakeAvailablePlugin("TestPlugin", "1.0.0"),
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
        public async Task PluginManager_RejectsUnsafeIdentifiersInCommands()
        {
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-commands-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            try
            {
                foreach (var identifier in new[] { "Plugin\ndelete:BTCPayServer.Plugins.Test", "Plugin\nTest", "Plugin\rTest", "Plugin:Test", "Plugin\0Test", "Plugin/Test", "Plugin\\Test", "./", "../Plugin", "..", ".", "", "commands", "Disabled", "disabled. " })
                {
                    Assert.Throws<InvalidOperationException>(() => PluginManager.QueueCommands(pluginDir, ("enable", identifier)));
                }
                Assert.Empty(PluginManager.GetPendingCommands(pluginDir));
                Assert.Throws<InvalidOperationException>(() => PluginManager.QueueCommands(pluginDir, ("enable", new string('a', 256))));
                // A name never resolves to the plugins folder itself, even at the file system root
                Assert.False(PluginManager.IsSafeName(Path.GetPathRoot(pluginDir)!, "."));

                // Any plugin folder name that loads can be disabled after a crash
                foreach (var identifier in new[] { "My Plugin", "Plugín" })
                {
                    Directory.CreateDirectory(Path.Combine(pluginDir, identifier));
                    PluginManager.DisablePlugin(pluginDir, identifier);
                }
                PluginManager.QueueCommands(pluginDir, ("install", "BTCPayServer.Plugins.Test"));
                var expected = new[] { ("disable", "My Plugin"), ("disable", "Plugín"), ("install", "BTCPayServer.Plugins.Test") };
                Assert.Equal(expected, PluginManager.GetPendingCommands(pluginDir));

                // Unsafe entries already in the commands file are ignored
                File.AppendAllLines(Path.Combine(pluginDir, "commands"), ["delete:..", "delete:./", "delete:disabled", "enable:", "install:Plugin:Test"]);
                Assert.Equal(expected, PluginManager.GetPendingCommands(pluginDir));

                // Catalog identifiers are checked before anything is downloaded or written
                var requested = false;
                using var httpClient = new HttpClient(new TestHttpMessageHandler(_ =>
                {
                    requested = true;
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                })) { BaseAddress = new Uri("https://plugins.example/") };
                var pluginService = CreatePluginService(pluginDir, [], httpClient);
                await Assert.ThrowsAsync<InvalidOperationException>(() => pluginService.DownloadRemotePlugin("../Plugin", null));
                Assert.False(requested);
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        [Fact]
        public async Task UploadPlugin_NamesRejectedFile()
        {
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-upload-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            try
            {
                using var httpClient = new HttpClient { BaseAddress = new Uri("https://plugins.example/") };
                var controller = new UIPluginManagerController(
                    CreatePluginService(pluginDir, [], httpClient), new PoliciesSettings(), new FormatStringLocalizer())
                {
                    TempData = new TempDataDictionary(new DefaultHttpContext(), new MemoryTempDataProvider())
                };
                static IFormFile Upload(string name, int length = 1) => new FormFile(new MemoryStream(new byte[length]), 0, length, "file", name);
                const string uploaded = "File uploaded, restart server to load plugin";
                const string rejected = "Not uploaded due to an invalid plugin file: ";

                // A missing file does not schedule an install
                Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>(await controller.UploadPlugin(null));

                // Reject invalid plugin names, file names and empty files without scheduling an install
                foreach (var file in new[] { Upload("commands.btcpay"), Upload("Disabled.btcpay"), Upload("Plugin:Test.btcpay"), Upload("Empty.btcpay", 0), Upload(".btcpay") })
                {
                    await controller.UploadPlugin(file);
                    var warning = controller.TempData.GetStatusMessageModel();
                    Assert.Equal(StatusMessageModel.StatusSeverity.Warning, warning.Severity);
                    Assert.Equal(rejected + file.FileName, warning.Message);
                    Assert.Empty(Directory.EnumerateFileSystemEntries(pluginDir));
                    Assert.Empty(PluginManager.GetPendingCommands(pluginDir));
                }

                // Long names are cut, so the message stays small
                await controller.UploadPlugin(Upload(new string('a', 2000) + ".btcpay"));
                Assert.Equal(rejected + new string('a', 300) + "...", controller.TempData.GetStatusMessageModel().Message);

                // A valid file is uploaded and scheduled for installation
                await controller.UploadPlugin(Upload("BTCPayServer.Plugins.Test.btcpay"));
                var status = controller.TempData.GetStatusMessageModel();
                Assert.Equal(StatusMessageModel.StatusSeverity.Success, status.Severity);
                Assert.Equal(uploaded, status.Message);
                Assert.True(File.Exists(Path.Combine(pluginDir, "BTCPayServer.Plugins.Test.btcpay")));
                Assert.Equal(new[] { ("install", "BTCPayServer.Plugins.Test") }, PluginManager.GetPendingCommands(pluginDir));
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExecuteCommands_KeepsInstalledPluginWhenPackageIsBroken(bool extractsPartly)
        {
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-install-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            try
            {
                static void WritePackage(string path, params string[] entries)
                {
                    using var package = ZipFile.Open(path, ZipArchiveMode.Create);
                    foreach (var entry in entries)
                        package.CreateEntry(entry);
                }

                // Two disabled plugins with an update each, the one for PluginA can't be extracted
                foreach (var plugin in new[] { "PluginA", "PluginB" })
                {
                    Directory.CreateDirectory(Path.Combine(pluginDir, plugin));
                    File.WriteAllText(Path.Combine(pluginDir, plugin, plugin + ".dll"), "installed");
                    File.WriteAllText(Path.Combine(pluginDir, plugin + ".json"), "{}");
                }
                File.WriteAllText(Path.Combine(pluginDir, "PluginB", "Removed.dll"), "installed");
                File.WriteAllLines(Path.Combine(pluginDir, "disabled"), ["PluginA", "PluginB"]);
                var package = Path.Combine(pluginDir, "PluginA.btcpay");
                // Either not a zip, or a zip whose second entry points outside the folder, so extraction stops halfway
                if (extractsPartly)
                    WritePackage(package, "PluginA.dll", "../PluginA.dll");
                else
                    File.WriteAllText(package, "not a zip");
                WritePackage(Path.Combine(pluginDir, "PluginB.btcpay"), "PluginB.dll");
                PluginManager.QueueCommands(pluginDir, ("install", "PluginA"), ("install", "PluginB"));
                var logger = LoggerFactory.CreateLogger(typeof(PluginManager));

                PluginManager.ExecuteCommands(pluginDir, logger);

                // The installed version stays as it was, and stays disabled
                Assert.Equal("installed", File.ReadAllText(Path.Combine(pluginDir, "PluginA", "PluginA.dll")));
                Assert.Equal(new[] { "PluginA" }, File.ReadAllLines(Path.Combine(pluginDir, "disabled")));
                // The next command still runs, and replaces the installed version as a whole
                Assert.Equal(new[] { "PluginB.dll", "PluginB.json" }, Directory.GetFiles(Path.Combine(pluginDir, "PluginB")).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal));
                Assert.Equal("", File.ReadAllText(Path.Combine(pluginDir, "PluginB", "PluginB.dll")));
                // The broken package is removed, and its command stays pending
                Assert.Equal(new[] { "PluginA", "PluginB", "commands", "disabled" }, Directory.GetFileSystemEntries(pluginDir).Select(entry => Path.GetFileName(entry)).Order(StringComparer.Ordinal));
                Assert.Equal(new[] { ("install", "PluginA") }, PluginManager.GetPendingCommands(pluginDir));
                // At the next start it finds no package, so it does nothing and doesn't enable the old version
                Assert.True(PluginManager.ExecuteCommands(pluginDir, logger));
                Assert.Equal(new[] { "PluginA" }, File.ReadAllLines(Path.Combine(pluginDir, "disabled")));
                Assert.Equal(new[] { "PluginA", "PluginB", "disabled" }, Directory.GetFileSystemEntries(pluginDir).Select(entry => Path.GetFileName(entry)).Order(StringComparer.Ordinal));
                Assert.False(PluginManager.ExecuteCommands(pluginDir, logger));
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        [Fact]
        public void ExecuteCommands_KeepsLaterCommandsOfAFailedPluginInOrder()
        {
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-order-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            try
            {
                Directory.CreateDirectory(Path.Combine(pluginDir, "PluginA"));
                File.WriteAllText(Path.Combine(pluginDir, "PluginA", "PluginA.dll"), "installed");
                File.WriteAllText(Path.Combine(pluginDir, "PluginA.btcpay"), "not a zip");
                PluginManager.QueueCommands(pluginDir, ("install", "PluginA"), ("delete", "PluginA"));
                var logger = LoggerFactory.CreateLogger(typeof(PluginManager));

                // The delete waits for the failed install, so it can't run before a later retry of it
                PluginManager.ExecuteCommands(pluginDir, logger);
                Assert.Equal(new[] { ("install", "PluginA"), ("delete", "PluginA") }, PluginManager.GetPendingCommands(pluginDir));
                Assert.True(Directory.Exists(Path.Combine(pluginDir, "PluginA")));

                // At the next start the install has no package, and the delete runs after it
                PluginManager.ExecuteCommands(pluginDir, logger);
                Assert.Empty(PluginManager.GetPendingCommands(pluginDir));
                Assert.False(Directory.Exists(Path.Combine(pluginDir, "PluginA")));
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        [Fact]
        public void ExecuteCommands_KeepsPackageUntilPluginIsEnabled()
        {
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-enable-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            try
            {
                // A disabled plugin with an update
                Directory.CreateDirectory(Path.Combine(pluginDir, "PluginA"));
                File.WriteAllText(Path.Combine(pluginDir, "PluginA", "PluginA.dll"), "installed");
                File.WriteAllText(Path.Combine(pluginDir, "PluginA.json"), "{}");
                using (var package = ZipFile.Open(Path.Combine(pluginDir, "PluginA.btcpay"), ZipArchiveMode.Create))
                    package.CreateEntry("PluginA.dll");
                var disabledFileName = Path.Combine(pluginDir, "disabled");
                File.WriteAllLines(disabledFileName, ["PluginA"]);
                PluginManager.QueueCommands(pluginDir, ("install", "PluginA"));
                var logger = LoggerFactory.CreateLogger(typeof(PluginManager));

                // Locked, so the enable fails. Unlike a read-only file, a lock also stops root
                using (File.Open(disabledFileName, FileMode.Open, FileAccess.Read, FileShare.None))
                    PluginManager.ExecuteCommands(pluginDir, logger);

                // The update is in place but still disabled, and its package and manifest stay with the pending command
                Assert.Equal("", File.ReadAllText(Path.Combine(pluginDir, "PluginA", "PluginA.dll")));
                Assert.Equal(new[] { "PluginA" }, File.ReadAllLines(disabledFileName));
                Assert.Equal(new[] { "PluginA", "PluginA.btcpay", "PluginA.json", "commands", "disabled" }, Directory.GetFileSystemEntries(pluginDir).Select(entry => Path.GetFileName(entry)).Order(StringComparer.Ordinal));
                Assert.Equal(new[] { ("install", "PluginA") }, PluginManager.GetPendingCommands(pluginDir));

                // At the next start it installs the package again and enables it
                Assert.True(PluginManager.ExecuteCommands(pluginDir, logger));
                Assert.Empty(File.ReadAllLines(disabledFileName));
                Assert.Equal(new[] { "PluginA.dll", "PluginA.json" }, Directory.GetFiles(Path.Combine(pluginDir, "PluginA")).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal));
                Assert.Equal(new[] { "PluginA", "disabled" }, Directory.GetFileSystemEntries(pluginDir).Select(entry => Path.GetFileName(entry)).Order(StringComparer.Ordinal));
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        [Theory]
        [InlineData("install")]
        [InlineData("enable")]
        public async Task InstalledPluginsViewModel_BlocksUninstallWhenPendingPluginHasNoManifest(string command)
        {
            InstalledPluginRequest[] requestedPlugins = null;
            using var httpClient = new HttpClient(new TestHttpMessageHandler(request =>
            {
                requestedPlugins = JsonConvert.DeserializeObject<InstalledPluginRequest[]>(
                    request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return TestHttpMessageHandler.JsonResponse("""
                                                           [{
                                                               "projectSlug": "dependent",
                                                               "buildId": 1,
                                                               "manifestInfo": {
                                                                   "identifier": "Dependent",
                                                                   "name": "Dependent",
                                                                   "version": "1.0.0",
                                                                   "dependencies": [{
                                                                       "identifier": "Dependency",
                                                                       "condition": ">=1.0.0"
                                                                   }]
                                                               },
                                                               "buildInfo": {}
                                                           }]
                                                           """);
            }));
            httpClient.BaseAddress = new Uri("https://plugins.example/");
            var pluginDir = Path.Combine(Path.GetTempPath(), $"btcpay-plugin-pending-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(pluginDir);
            try
            {
                var disabled = command == "enable"
                    ? new Dictionary<string, Version> { ["Dependent"] = null }
                    : null;
                WritePluginState(pluginDir, disabled, (command, "Dependent"), null);
                var controller = CreatePluginManagerController(
                    pluginDir,
                    [MakeLoadedPlugin("Dependency")],
                    httpClient);

                var model = await controller.CreateInstalledPluginsViewModel();

                Assert.NotNull(requestedPlugins);
                Assert.Equal(2, requestedPlugins.Length);
                Assert.Contains(requestedPlugins,
                    plugin => plugin.Identifier == "Dependency" && plugin.Version == "1.0.0");
                Assert.Contains(requestedPlugins,
                    plugin => plugin.Identifier == "Dependent" && plugin.Version == "0.0.0");
                var plugin = Assert.Single(model.InstalledPlugins);
                var blockedAction = Assert.Single(plugin.Actions);
                Assert.Null(blockedAction.FormAction);
                Assert.NotNull(blockedAction.Tooltip);
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
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
            HttpMethod requestedMethod = null;
            using var httpClient = new HttpClient(new TestHttpMessageHandler(request =>
            {
                requestedUri = request.RequestUri;
                requestedMethod = request.Method;
                return TestHttpMessageHandler.JsonResponse("[]");
            }));
            PluginManagerPlugin.ConfigurePluginBuilderClient(
                new PoliciesSettings { PluginSource = "https://plugins.example.com/catalog?tenant=one#section" },
                httpClient);

            await new PluginBuilderClient(httpClient).GetInstalledPluginsUpdates(
                "2.3.7",
                false,
                [new InstalledPluginRequest("TestPlugin", "1.0.0")]);

            Assert.Equal("https://plugins.example.com/catalog/", httpClient.BaseAddress.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, requestedMethod);
            Assert.Equal("/catalog/api/v1/plugins/updates", requestedUri.AbsolutePath);
            Assert.Contains("btcpayVersion=2.3.7", requestedUri.Query);
            Assert.False(bool.Parse(QueryHelpers.ParseQuery(requestedUri.Query)["includePreRelease"].ToString()));
            Assert.DoesNotContain("tenant=one", requestedUri.Query);
            Assert.Empty(requestedUri.Fragment);
        }

        [Fact]
        public async Task LatestVersionsForInstalledPlugins_RequestsOnlyEligibleInstalledPlugins()
        {
            var systemPlugin = MakeLoadedPlugin("SystemPlugin");
            systemPlugin.SystemPlugin = true;
            var requestCount = 0;
            Uri requestedUri = null;
            HttpMethod requestedMethod = null;
            InstalledPluginRequest[] requestedPlugins = null;
            using var httpClient = new HttpClient(new TestHttpMessageHandler(request =>
            {
                requestCount++;
                requestedUri = request.RequestUri;
                requestedMethod = request.Method;
                requestedPlugins = JsonConvert.DeserializeObject<InstalledPluginRequest[]>(
                    request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return TestHttpMessageHandler.JsonResponse("""
                                                           [{
                                                               "projectSlug": "loaded-plugin",
                                                               "buildId": 1,
                                                               "manifestInfo": {
                                                                   "identifier": "loadedplugin",
                                                                   "name": "Loaded Plugin",
                                                                   "version": "1.1.0"
                                                               },
                                                               "buildInfo": {}
                                                           }]
                                                           """);
            }))
            {
                BaseAddress = new Uri("https://plugins.example/")
            };
            var pluginService = CreatePluginService(
                Path.GetTempPath(),
                [MakeLoadedPlugin("LoadedPlugin"), systemPlugin],
                httpClient,
                new PoliciesSettings { PluginPreReleases = true });

            var updates = await pluginService.GetLatestVersionsForInstalledPlugins(
                new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase)
                {
                    ["loadedplugin"] = new Version(0, 9, 0),
                    ["DisabledPlugin"] = new Version(2, 0, 0),
                    ["UnknownVersionPlugin"] = null
                });

            Assert.Equal(1, requestCount);
            Assert.NotNull(requestedUri);
            Assert.Equal(HttpMethod.Post, requestedMethod);
            Assert.Equal("/api/v1/plugins/updates", requestedUri.AbsolutePath);
            var query = QueryHelpers.ParseQuery(requestedUri.Query);
            Assert.Equal(
                BTCPayServerEnvironment.GetInformationalVersion().TrimStart('v').Split('+')[0],
                query["btcpayVersion"].ToString());
            Assert.True(bool.Parse(query["includePreRelease"].ToString()));
            Assert.NotNull(requestedPlugins);
            Assert.Equal(2, requestedPlugins.Length);
            Assert.Single(requestedPlugins, plugin => plugin.Identifier == "LoadedPlugin" && plugin.Version == "1.0.0");
            Assert.Single(requestedPlugins, plugin => plugin.Identifier == "DisabledPlugin" && plugin.Version == "2.0.0");
            Assert.DoesNotContain(requestedPlugins, plugin => plugin.Identifier == "SystemPlugin");
            Assert.DoesNotContain(requestedPlugins, plugin => plugin.Identifier == "UnknownVersionPlugin");

            var update = Assert.Single(updates);
            Assert.Equal("loadedplugin", update.Identifier);
            Assert.Equal(new Version(1, 1, 0), update.Version);
        }

        [Fact]
        public async Task LatestVersionsForInstalledPlugins_DoesNotCallBuilderWhenNoEligiblePlugins()
        {
            var systemPlugin = MakeLoadedPlugin("SystemPlugin");
            systemPlugin.SystemPlugin = true;
            using var httpClient = new HttpClient(new TestHttpMessageHandler(_ =>
                throw new InvalidOperationException("The plugin builder should not be called.")))
            {
                BaseAddress = new Uri("https://plugins.example/")
            };
            var pluginService = CreatePluginService(Path.GetTempPath(), [systemPlugin], httpClient);

            var updates = await pluginService.GetLatestVersionsForInstalledPlugins(
                new Dictionary<string, Version> { ["UnknownVersionPlugin"] = null });

            Assert.Empty(updates);
        }

        [Fact]
        public async Task LatestVersionsForInstalledPlugins_PropagatesCancellation()
        {
            var handler = new BlockingHttpMessageHandler();
            using var httpClient = new HttpClient(handler);
            httpClient.BaseAddress = new Uri("https://plugins.example/");
            var pluginService = CreatePluginService(
                Path.GetTempPath(),
                [MakeLoadedPlugin("TestPlugin")],
                httpClient);
            using var cancellationTokenSource = new CancellationTokenSource();

            var lookupTask = pluginService.GetLatestVersionsForInstalledPlugins(
                new Dictionary<string, Version>(),
                cancellationToken: cancellationTokenSource.Token);
            await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationTokenSource.Token);
            await cancellationTokenSource.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                lookupTask.WaitAsync(TimeSpan.FromSeconds(1)));
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

        private static HttpResponseMessage CreatePublishedVersionResponse(
            string slug,
            string identifier,
            string name,
            string version)
        {
            var body = JsonConvert.SerializeObject(new
            {
                projectSlug = slug,
                buildId = 1,
                manifestInfo = new
                {
                    identifier,
                    name,
                    version
                },
                buildInfo = new { }
            });
            return TestHttpMessageHandler.JsonResponse(body);
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
            PluginService.AvailablePlugin selectedPlugin = null,
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
                return controller.CreatePluginDirectoryViewModel(selectedPlugin);
            }
            finally
            {
                Directory.Delete(pluginDir, true);
            }
        }

        private UIPluginManagerController CreatePluginManagerController(
            string pluginDir,
            IEnumerable<IBTCPayServerPlugin> loadedPlugins,
            HttpClient httpClient,
            PoliciesSettings policiesSettings = null)
        {
            policiesSettings ??= new PoliciesSettings();
            var pluginService = CreatePluginService(
                pluginDir,
                loadedPlugins,
                httpClient,
                policiesSettings);
            return new UIPluginManagerController(pluginService, policiesSettings, null);
        }

        private PluginService CreatePluginService(
            string pluginDir,
            IEnumerable<IBTCPayServerPlugin> loadedPlugins,
            HttpClient httpClient,
            PoliciesSettings policiesSettings = null)
        {
            policiesSettings ??= new PoliciesSettings();
            return new PluginService(
                loadedPlugins,
                new PluginBuilderClient(httpClient),
                Options.Create(new DataDirectories { PluginDir = pluginDir }),
                policiesSettings,
                new BTCPayServerEnvironment(null, CreateNetworkProvider(ChainName.Regtest), null, new BTCPayServerOptions()));
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

        private sealed class BlockingHttpMessageHandler : HttpMessageHandler
        {
            public TaskCompletionSource<bool> RequestStarted { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The request should have been cancelled.");
            }
        }

        private sealed class FormatStringLocalizer : IStringLocalizer
        {
            public LocalizedString this[string name] => new(name, name);
            public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        }

        private sealed class MemoryTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
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
