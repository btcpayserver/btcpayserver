using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins
{
    public class BTCPayServerPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier { get; } = nameof(BTCPayServer);
        public override string Name { get; } = "BTCPay Server";
        public override string Description { get; } = "BTCPay Server core system";

        public override void Execute(IServiceCollection services)
        {
            services.AddRegexRouteConvention("apiKeyId", "^akid_[0-9a-f]{16}\\z");
            services.AddRegexRouteConvention("storeId", RouteRegexPatterns.Base58);
            services.AddRegexRouteConvention("invoiceId", RouteRegexPatterns.Base58);
            services.AddRegexRouteConvention("appId", RouteRegexPatterns.Base58);
            services.AddRegexRouteConvention("pullPaymentId", RouteRegexPatterns.Base58);
            services.AddRegexRouteConvention("payoutId", RouteRegexPatterns.Base58);
            services.AddRegexRouteConvention("paymentRequestId", RouteRegexPatterns.GuidD);
            services.AddRegexRouteConvention("paymentHash", RouteRegexPatterns.Hex64);
        }
    }
}
