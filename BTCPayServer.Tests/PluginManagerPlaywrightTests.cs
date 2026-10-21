using System;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins;
using BTCPayServer.Tests.Plugins;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

[Trait("Playwright", "Playwright")]
[Collection(nameof(NonParallelizableCollectionDefinition))]
public class PluginManagerPlaywrightTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    public async Task ShowsRuntimeFailureForAutomaticallyDisabledPlugin()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await InstallCrashPlugin(s, new Version(1, 2, 3));

        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.SkipWizard();

        await s.GoToUrl(CrashPlugin.CrashPath, ignoreResponse: true);
        await s.Server.PayTester.RestartAsync();

        await s.GoToUrl("/server/plugins");
        var disabledPlugin = s.Page.Locator("li.list-group-item")
            .Filter(new LocatorFilterOptions { HasText = CrashPlugin.PluginIdentifier });
        await Expect(disabledPlugin).ToBeVisibleAsync();
        await disabledPlugin.Locator(".plugin-failure__summary").ClickAsync();
        var failure = disabledPlugin.Locator(".plugin-failure");
        await Expect(failure).ToContainTextAsync("Runtime request");
        await Expect(failure).ToContainTextAsync("Confirmed");
        await Expect(failure).ToContainTextAsync(CrashPlugin.FailureMessage);
        await Expect(failure.Locator(".plugin-failure__details"))
            .ToContainTextAsync(nameof(CrashPlugin));
        await Expect(failure.Locator(".plugin-failure__copy")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DisablesPluginWhenManifestVersionDoesNotMatch()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await InstallCrashPlugin(s, new Version(9, 9));

        await Assert.ThrowsAsync<ConfigException>(() => s.Server.StartAsync());
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.SkipWizard();

        await s.GoToUrl("/server/plugins");
        var disabledPlugin = s.Page.Locator("li.list-group-item")
            .Filter(new LocatorFilterOptions { HasText = CrashPlugin.PluginIdentifier });
        await Expect(disabledPlugin).ToBeVisibleAsync();
        await disabledPlugin.Locator(".plugin-failure__summary").ClickAsync();
        var failure = disabledPlugin.Locator(".plugin-failure");
        await Expect(failure).ToContainTextAsync("Plugin validation");
        await Expect(failure).ToContainTextAsync("The plugin version does not match its manifest.");
        await Expect(failure).ToContainTextAsync("Expected 9.9, but loaded 1.2.3");
    }

    [Fact]
    public async Task DisablesPluginWhenManifestIdentifierDoesNotMatch()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        const string manifestIdentifier = "BTCPayServer.Tests.UnexpectedPlugin";
        await InstallCrashPlugin(s, new Version(1, 2, 3), manifestIdentifier);

        await Assert.ThrowsAsync<ConfigException>(() => s.Server.StartAsync());
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.SkipWizard();

        await s.GoToUrl("/server/plugins");
        var disabledPlugin = s.Page.Locator("li.list-group-item")
            .Filter(new LocatorFilterOptions { HasText = manifestIdentifier });
        await Expect(disabledPlugin).ToBeVisibleAsync();
        await disabledPlugin.Locator(".plugin-failure__summary").ClickAsync();
        var failure = disabledPlugin.Locator(".plugin-failure");
        await Expect(failure).ToContainTextAsync("Plugin discovery");
        await Expect(failure).ToContainTextAsync("The plugin identifier does not match.");
        await Expect(failure).ToContainTextAsync(
            $"Expected {manifestIdentifier}, but found: {CrashPlugin.PluginIdentifier}");
    }

    private static async Task InstallCrashPlugin(
        PlaywrightTester s,
        Version manifestVersion,
        string manifestIdentifier = CrashPlugin.PluginIdentifier)
    {
        var pluginDir = Path.Combine(TestUtils.TestDirectory, s.Server.Scope, "pay", "plugins");
        var installedPluginDir = Path.Combine(pluginDir, CrashPlugin.PluginIdentifier);
        Directory.CreateDirectory(installedPluginDir);
        File.Copy(
            typeof(CrashPlugin).Assembly.Location,
            Path.Combine(installedPluginDir, CrashPlugin.PluginIdentifier + ".dll"));
        await File.WriteAllTextAsync(
            Path.Combine(installedPluginDir, CrashPlugin.PluginIdentifier + ".json"),
            JsonConvert.SerializeObject(new PluginService.AvailablePlugin
            {
                Identifier = manifestIdentifier,
                Name = "Crashing test plugin",
                Version = manifestVersion,
                Description = "Throws an expected exception for plugin crash diagnostics tests."
            }));
        s.Server.PayTester.PluginDir = pluginDir;
    }
}
