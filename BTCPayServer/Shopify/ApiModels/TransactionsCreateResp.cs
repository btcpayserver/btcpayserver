using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Services.Shopify.ApiModels.DataHolders;

namespace BTCPayServer.Services.Shopify.ApiModels
{
    public class TransactionsCreateResp
    {
        public TransactionDataHolder transaction { get; set; }
    }
}
