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
            var options = new OpenIdOptions().Load(configuration);
            if(!options.EnforceClients)
            {
                builder.AcceptAnonymousClients();
            }
            if(!options.EnforceGrantTypes)
            {
                builder.IgnoreGrantTypePermissions();
            }
            if(!options.EnforceScopes)
            {
                builder.IgnoreScopePermissions();
            }
            if(!options.EnforceEndpoints)
            {
                builder.IgnoreEndpointPermissions();
            }
        }
    }
}
