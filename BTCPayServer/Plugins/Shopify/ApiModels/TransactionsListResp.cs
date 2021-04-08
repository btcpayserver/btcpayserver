using System.Collections.Generic;
using BTCPayServer.Plugins.Shopify.ApiModels.DataHolders;

namespace BTCPayServer.Plugins.Shopify.ApiModels
{
    public class TransactionsListResp
    {
        public List<TransactionDataHolder> transactions { get; set; }
    }
}
