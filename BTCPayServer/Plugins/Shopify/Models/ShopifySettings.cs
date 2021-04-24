using System;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Shopify.Models
{
    public class ShopifySettings
    {
        [Display(Name = "Shop Name")]
        public string ShopName { get; set; }
        public string ApiKey { get; set; }
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
                return ShopName?.Contains(".", StringComparison.OrdinalIgnoreCase) is true ? $"https://{ShopName}.myshopify.com" : ShopName;
            }
        }
    }
}
