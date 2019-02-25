using BTCPayServer.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer
{
    public static class OpenIddictServerBuilderExtensions
    {
        public static void ConfigureClientRequirements(this OpenIddictServerBuilder builder,
            IConfiguration configuration)
        {
            if(!OpenIdOptions.GetOpenIdEnforceClients(configuration))
            {
                builder.AcceptAnonymousClients();
            }
            if(!OpenIdOptions.GetOpenIdEnforceGrantTypes(configuration))
            {
                builder.IgnoreGrantTypePermissions();
            }
            if(!OpenIdOptions.GetOpenIdEnforceScopes(configuration))
            {
                builder.IgnoreScopePermissions();
            }
            if(!OpenIdOptions.GetOpenIdEnforceEndpoints(configuration))
            {
                builder.IgnoreEndpointPermissions();
            }
        }
    }
}
