using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

[Trait("Playwright", "Playwright")]
[Collection(nameof(NonParallelizableCollectionDefinition))]
public class PluginManagerPlaywrightTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    private const string PluginIdentifier = "BTCPayServer.Tests.CrashPlugin";
    private const string CrashPath = "/test/crashing-plugin";
    private const string FailureMessage = "Expected runtime failure from the crashing test plugin.";

    [Fact]
    public async Task ShowsRuntimeFailureForAutomaticallyDisabledPlugin()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await InstallCrashPlugin(s, new Version(1, 2, 3));

        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.SkipWizard();

        await s.GoToUrl(CrashPath, ignoreResponse: true);
        await s.Server.PayTester.RestartAsync();

        await s.GoToUrl("/server/plugins");
        var disabledPlugin = s.Page.Locator("li.list-group-item")
            .Filter(new LocatorFilterOptions { HasText = PluginIdentifier });
        await Expect(disabledPlugin).ToBeVisibleAsync();
        await disabledPlugin.Locator(".plugin-failure__summary").ClickAsync();
        var failure = disabledPlugin.Locator(".plugin-failure");
        await Expect(failure).ToContainTextAsync("Runtime request");
        await Expect(failure).ToContainTextAsync("Confirmed");
        await Expect(failure).ToContainTextAsync(FailureMessage);
        await Expect(failure.Locator(".plugin-failure__details"))
            .ToContainTextAsync("CrashPlugin");
        var copy = failure.Locator(".plugin-failure__copy");
        await copy.ClickAsync();
        await Expect(copy).ToContainTextAsync("Copied");
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
            .Filter(new LocatorFilterOptions { HasText = PluginIdentifier });
        await Expect(disabledPlugin).ToBeVisibleAsync();
        await disabledPlugin.Locator(".plugin-failure__summary").ClickAsync();
        var failure = disabledPlugin.Locator(".plugin-failure");
        await Expect(failure).ToContainTextAsync("Plugin validation");
        await Expect(failure).ToContainTextAsync("The plugin version does not match its manifest.");
        await Expect(failure).ToContainTextAsync("Expected 9.9, but loaded 1.2.3");
    }

    [Fact]
    public async Task DisablesPluginWhenAssemblyIdentifierDoesNotMatch()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        const string assemblyIdentifier = "BTCPayServer.Tests.UnexpectedPlugin";
        await InstallCrashPlugin(s, new Version(1, 2, 3), assemblyIdentifier);

        await Assert.ThrowsAsync<ConfigException>(() => s.Server.StartAsync());
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.SkipWizard();

        await s.GoToUrl("/server/plugins");
        var disabledPlugin = s.Page.Locator("li.list-group-item")
            .Filter(new LocatorFilterOptions { HasText = PluginIdentifier });
        await Expect(disabledPlugin).ToBeVisibleAsync();
        await disabledPlugin.Locator(".plugin-failure__summary").ClickAsync();
        var failure = disabledPlugin.Locator(".plugin-failure");
        await Expect(failure).ToContainTextAsync("Plugin discovery");
        await Expect(failure).ToContainTextAsync("The plugin identifier does not match.");
        await Expect(failure).ToContainTextAsync(
            $"Expected {PluginIdentifier}, but found: {assemblyIdentifier}");
    }

    private static async Task InstallCrashPlugin(
        PlaywrightTester s,
        Version manifestVersion,
        string assemblyIdentifier = PluginIdentifier)
    {
        var pluginDir = Path.Combine(TestUtils.TestDirectory, s.Server.Scope, "pay", "plugins");
        var installedPluginDir = Path.Combine(pluginDir, PluginIdentifier);
        Directory.CreateDirectory(installedPluginDir);
        CompileCrashPlugin(
            Path.Combine(installedPluginDir, PluginIdentifier + ".dll"),
            assemblyIdentifier);
        await File.WriteAllTextAsync(
            Path.Combine(installedPluginDir, PluginIdentifier + ".json"),
            JsonConvert.SerializeObject(new PluginService.AvailablePlugin
            {
                Identifier = PluginIdentifier,
                Name = "Crashing test plugin",
                Version = manifestVersion,
                Description = "Throws an expected exception for plugin crash diagnostics tests."
            }));
        s.Server.PayTester.PluginDir = pluginDir;
    }

    private static void CompileCrashPlugin(string assemblyPath, string assemblyIdentifier)
    {
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using BTCPayServer.Abstractions.Models;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            namespace BTCPayServer.Tests.Plugins;

            public class CrashPlugin : BaseBTCPayServerPlugin
            {
                public override string Identifier => "{{assemblyIdentifier}}";
                public override string Name => "Crashing test plugin";
                public override string Description => "Throws an expected exception for plugin crash diagnostics tests.";
                public override Version Version => new(1, 2, 3);

                public override void Execute(IApplicationBuilder applicationBuilder,
                    IServiceProvider applicationBuilderApplicationServices)
                {
                    applicationBuilder.Map("{{CrashPath}}", crashApp => crashApp.Run(Crash));
                }

                private static Task Crash(HttpContext context)
                {
                    throw new InvalidOperationException("{{FailureMessage}}");
                }
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(BaseBTCPayServerPlugin).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            $"{PluginIdentifier}.{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(source, path: "CrashPlugin.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(assemblyPath);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
