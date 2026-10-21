using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Tests.Plugins;

public class CrashPlugin : BaseBTCPayServerPlugin
{
    public const string PluginIdentifier = "BTCPayServer.Tests.CrashPlugin";
    public const string CrashPath = "/test/crashing-plugin";
    public const string FailureMessage = "Expected runtime failure from the crashing test plugin.";

    public override string Identifier => PluginIdentifier;
    public override string Name => "Crashing test plugin";
    public override string Description => "Throws an expected exception for plugin crash diagnostics tests.";
    public override Version Version => new(1, 2, 3);

    public override void Execute(IApplicationBuilder applicationBuilder,
        IServiceProvider applicationBuilderApplicationServices)
    {
        applicationBuilder.Map(CrashPath, crashApp => crashApp.Run(Crash));
    }

    private static Task Crash(HttpContext context)
    {
        throw new InvalidOperationException(FailureMessage);
    }
}
