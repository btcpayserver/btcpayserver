using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Hosting
{
    public static class OpenIddictServerBuilderExtensions
    {
        
        public static void ConfigureClientRequirements(this OpenIddictServerBuilder builder,
            IConfiguration configuration)
        {
            if(!configuration.GetValue<bool>("openid_enforce_clientId", false))
            {
                builder.AcceptAnonymousClients();
            }
            if(!configuration.GetValue<bool>("openid_enforce_grant_type", false))
            {
                builder.IgnoreGrantTypePermissions();
            }
            if(!configuration.GetValue<bool>("openid_enforce_scope", false))
            {
                
                builder.IgnoreScopePermissions();
            }
            if(!configuration.GetValue<bool>("openid_enforce_scope", false))
            {
                builder.IgnoreEndpointPermissions();
            }
        }
    }
}