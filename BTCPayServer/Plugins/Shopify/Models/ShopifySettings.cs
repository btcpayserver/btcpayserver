using System;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Shopify.Models
{
    public class ShopifySettings
    {
        [Display(Name = "Shop Name")]
        public string ShopName { get; set; }

        [Display(Name = "API Key")]
        public string ApiKey { get; set; }

        [Display(Name = "Admin API access token")]
        public string Password { get; set; }

        public bool CredentialsPopulated()
        {
            return
                !string.IsNullOrWhiteSpace(ShopName) &&
                !string.IsNullOrWhiteSpace(ApiKey) &&
                !string.IsNullOrWhiteSpace(Password);
        }
        public DateTimeOffset? IntegratedAt { get; set; }

        [JsonIgnore]
        public string ShopifyUrl
        {
            get
            {
                return ShopName?.Contains('.', StringComparison.OrdinalIgnoreCase) is true ? ShopName : $"https://{ShopName}.myshopify.com";
            }
        }
    }
}
