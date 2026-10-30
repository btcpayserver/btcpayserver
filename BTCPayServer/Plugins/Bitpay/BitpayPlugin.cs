#nullable enable
using System;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Bitpay.Controllers;
using BTCPayServer.Plugins.Bitpay.Security;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Plugins.Bitpay;

public class BitpayPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Bitpay";
    public const string RateLimitZone = "legacytokens";
    public override string Identifier => "BTCPayServer.Plugins.Bitpay";
    public override string Name => "Bitpay";
    public override string Description => "Add a compatibility layer to the legacy Bitpay API";

    public override void Execute(IApplicationBuilder applicationBuilder, IServiceProvider applicationBuilderApplicationServices)
    {
        var rateLimits = applicationBuilderApplicationServices.GetRequiredService<IRateLimitService>();
        var environment = applicationBuilderApplicationServices.GetRequiredService<IHostEnvironment>();
        rateLimits.SetZone(environment.IsDevelopment()
            ? $"zone={RateLimitZone} rate=1000r/min burst=100 nodelay"
            : $"zone={RateLimitZone} rate=5r/min burst=5 nodelay");
    }

    public override void Execute(IServiceCollection services)
    {
        services.AddPolicyDefinitions(
            new PolicyDefinition(
                BitpayPolicies.CanManageLegacyAccessTokens,
                new PermissionDisplay("Manage legacy access tokens", "Allows managing the legacy access tokens of all your stores."),
                new PermissionDisplay("Manage selected stores' legacy access tokens", "Allows managing the legacy access tokens of the selected stores."),
                includedByPermissions: [Policies.CanModifyStoreSettings]));
        services.AddSingleton<IHostedService, BitpayIPNSender>();
        var userAgent = BTCPayServerEnvironment.GetUserAgentHeaderValue();
        services.AddHttpClient(BitpayIPNSender.NamedClient)
            .ConfigureHttpClient(client =>
            {
                client.DefaultRequestHeaders.UserAgent.Add(userAgent);
            })
            .UseSSRFProtection();

        services.AddSingleton<MatcherPolicy, BitpayEndpointSelectorPolicy>();
        services.TryAddSingleton<TokenRepository>();
        services.AddScheduledDbScript("Expired BitPay Pairing Code Cleanup",
            """
            WITH deleted_pairing_codes AS (
                DELETE FROM "PairingCodes"
                WHERE "Expiration" < @now
                RETURNING 1
            )
            SELECT COUNT(*) FROM deleted_pairing_codes;
            """);
        services.AddTransient<BitpayAccessTokenController>();
        services.AddScoped<IAuthorizationHandler, BitpayAuthorizationHandler>();
        services.AddAuthentication()
            .AddScheme<BitpayAuthenticationOptions, BitpayAuthenticationHandler>(AuthenticationSchemes.Bitpay, o => { });
        services.AddUIExtension("store-category-nav", "/Plugins/Bitpay/Views/NavExtension.cshtml");

        services.AddStaticSearch(new ActionResultItemViewModel()
        {
            RequiredPolicy = BitpayPolicies.CanManageLegacyAccessTokens,
            Title = "View the access tokens (for legacy API access)",
            Action = nameof(UIStoresTokenController.ListTokens),
            Controller = "UIStoresToken",
            Values = (ctx) => new { storeId = ctx.Store!.Id, area = Area },
            Category = "Store",
            Aliases = new[] { "Tokens" }
        });
    }
}
