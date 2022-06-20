using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Custodians.MockCustodian
{
    public class MockCustodianPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier { get; } = "BTCPayServer.Plugins.Custodians.Mock";
        public override string Name { get; } = "Custodian: Mock";
        public override string Description { get; } = "Adds a Mock custodian for testing";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<MockCustodian>();
            services.AddSingleton<ICustodian, MockCustodian>(provider => provider.GetRequiredService<MockCustodian>());
        }
    }
}
