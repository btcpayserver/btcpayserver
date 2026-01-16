using BTCPayServer.Abstractions.Models;
using BTCPayServer.Models;

namespace BTCPayServer.Plugins
{
    public class BTCPayServerPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier { get; } = nameof(BTCPayServer);
        public override string Name { get; } = "BTCPay Server";
        public override string Description { get; } = "BTCPay Server core system";

    }
}
