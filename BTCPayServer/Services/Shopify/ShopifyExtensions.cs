using BTCPayServer.Services.Shopify.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Services.Shopify
{
    public static class ShopifyExtensions
    {
        public static ShopifyApiClientCredentials CreateShopifyApiCredentials(this ShopifySettings shopify)
        {
            return new ShopifyApiClientCredentials
            {
                ShopName = shopify.ShopName,
                ApiKey = shopify.ApiKey,
                ApiPassword = shopify.Password
            };
        }

        public static void AddShopify(this IServiceCollection services)
        {
            services.AddSingleton<IHostedService, ShopifyOrderMarkerHostedService>();
        }
    }
}
