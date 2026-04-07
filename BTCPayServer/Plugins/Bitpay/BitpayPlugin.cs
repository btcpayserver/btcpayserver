#nullable enable
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Bitpay.Controllers;
using BTCPayServer.Plugins.Bitpay.Security;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.Bitpay;

public class BitpayPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Bitpay";
    public override string Identifier => "BTCPayServer.Plugins.Bitpay";
    public override string Name => "Bitpay";
    public override string Description => "Add a compatibility layer to the legacy Bitpay API";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<IHostedService, BitpayIPNSender>();
        var userAgent = BTCPayServerEnvironment.GetUserAgentHeaderValue();
        services.AddHttpClient(BitpayIPNSender.NamedClient)
            .ConfigureHttpClient(client =>
            {
                client.DefaultRequestHeaders.UserAgent.Add(userAgent);
            });

        services.AddSingleton<MatcherPolicy, BitpayEndpointSelectorPolicy>();
        services.TryAddSingleton<TokenRepository>();
        services.AddTransient<BitpayAccessTokenController>();
        services.AddScoped<IAuthorizationHandler, BitpayAuthorizationHandler>();
        services.AddAuthentication()
            .AddScheme<BitpayAuthenticationOptions, BitpayAuthenticationHandler>(AuthenticationSchemes.Bitpay, o => { });
        services.AddUIExtension("store-category-nav", "/Plugins/Bitpay/Views/NavExtension.cshtml");

        services.AddStaticSearch(new ActionResultItemViewModel()
        {
            RequiredPolicy = Policies.CanViewStoreSettings,
            Title = "Access Tokens",
            Action = nameof(UIStoresTokenController.ListTokens),
            Controller = "UIStoresToken",
            Values = (ctx) => new { storeId = ctx.Store!.Id, area = Area },
            Category = "Store",
            Keywords = new[] { "Tokens" }
        });
    }
}
