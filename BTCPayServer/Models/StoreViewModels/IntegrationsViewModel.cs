using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Services.Shopify.Models;
using static BTCPayServer.Data.StoreBlob;

namespace BTCPayServer.Models.StoreViewModels
{
    public class IntegrationsViewModel
    {
        public ShopifySettings Shopify { get; set; }
    }
}
