using System.Collections.Generic;
using BTCPayServer.Services.Shopify.ApiModels.DataHolders;

namespace BTCPayServer.Services.Shopify.ApiModels
{
    public class TransactionsListResp
    {
        public List<TransactionDataHolder> transactions { get; set; }
    }
}
