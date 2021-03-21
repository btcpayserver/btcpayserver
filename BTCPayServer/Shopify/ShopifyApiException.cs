using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Shopify
{
    public class ShopifyApiException : Exception
    {
        public ShopifyApiException(string message) : base(message)
        {
        }
    }
}
