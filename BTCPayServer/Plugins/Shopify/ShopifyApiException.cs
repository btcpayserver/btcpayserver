using System;

namespace BTCPayServer.Plugins.Shopify
{
    public class ShopifyApiException : Exception
    {
        public ShopifyApiException(string message) : base(message)
        {
        }
    }
}
