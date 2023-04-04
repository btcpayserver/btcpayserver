#if DEBUG
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.FakeCustodian;

public class FakeCustodianPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier { get; } = "BTCPayServer.Plugins.Custodians.Fake";
    public override string Name { get; } = "Custodian: Fake";
    public override string Description { get; } = "Adds a fake custodian for testing";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<FakeCustodian>();
        services.AddSingleton<ICustodian, FakeCustodian>(provider => provider.GetRequiredService<FakeCustodian>());
    }
}
#endif
