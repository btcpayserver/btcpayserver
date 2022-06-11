using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Custodians.Kraken.Kraken;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Custodians.Kraken
{
    public class KrakenCustodianPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier { get; } = "BTCPayServer.Plugins.Custodians.Kraken";
        public override string Name { get; } = "Custodian: Kraken";
        public override string Description { get; } = "Adds Kraken support for the Custodian system";

        public override void Execute(IServiceCollection services)
        {
            // List all known custodians after registering CustodianRegistry... 
            services.AddSingleton<KrakenExchange>();
            services.AddSingleton<ICustodian, KrakenExchange>(provider => provider.GetRequiredService<KrakenExchange>());
        }
    }
}
