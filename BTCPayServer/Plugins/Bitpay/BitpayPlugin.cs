#nullable enable
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Bitpay.Controllers;
using BTCPayServer.Plugins.Bitpay.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BTCPayServer.Plugins.Bitpay;

public class BitpayPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Bitpay";
    public override string Identifier => "BTCPayServer.Plugins.Bitpay";
    public override string Name => "Bitpay";
    public override string Description => "Add a compatibility layer to the legacy Bitpay API";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<MatcherPolicy, BitpayEndpointSelectorPolicy>();
        services.TryAddSingleton<TokenRepository>();
        services.AddTransient<BitpayAccessTokenController>();
        services.AddScoped<IAuthorizationHandler, BitpayAuthorizationHandler>();
        services.AddAuthentication()
            .AddScheme<BitpayAuthenticationOptions, BitpayAuthenticationHandler>(AuthenticationSchemes.Bitpay, o => { });
        services.AddUIExtension("store-category-nav", "/Plugins/Bitpay/Views/NavExtension.cshtml");
    }
}
