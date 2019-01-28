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
            if(!configuration.GetOpenIdEnforceClients())
            {
                builder.AcceptAnonymousClients();
            }
            if(!configuration.GetOpenIdEnforceGrantTypes())
            {
                builder.IgnoreGrantTypePermissions();
            }
            if(!configuration.GetOpenIdEnforceScopes())
            {
                builder.IgnoreScopePermissions();
            }
            if(!configuration.GetOpenIdEnforceEndpoints())
            {
                builder.IgnoreEndpointPermissions();
            }
        }
    }
}
