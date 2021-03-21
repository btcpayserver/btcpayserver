using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Data;
using BTCPayServer.Services.Shopify.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Shopify
{
    public static class ShopifyExtensions
    {
        public const string StoreBlobKey = "shopify";
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
            
            services.AddSingleton<IUIExtension>(new UIExtension("Shopify/StoreIntegrationShopifyOption", "store-integrations-list"));
        }

        public static ShopifySettings GetShopifySettings(this StoreBlob storeBlob)
        {
            if (storeBlob.AdditionalData.TryGetValue(StoreBlobKey, out var rawS))
            {
                return new Serializer(null).ToObject<ShopifySettings>(rawS as JObject);
            }

            return null;
        }
        public static void SetShopifySettings(this StoreBlob storeBlob, ShopifySettings settings)
        {
            if (settings is null)
            {
                storeBlob.AdditionalData.Remove(StoreBlobKey);
            }
            else
            {
                storeBlob.AdditionalData.AddOrReplace(StoreBlobKey, new Serializer(null).ToString(settings));
            }
        }
    }
}
