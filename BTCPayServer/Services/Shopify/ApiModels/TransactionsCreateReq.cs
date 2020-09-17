using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Shopify.ApiModels
{
    public class TransactionsCreateReq
    {
        public DataHolder transaction { get; set; }

        public class DataHolder
        {
            public string currency { get; set; }
            public string amount { get; set; }
            public string kind { get; set; }
            public long? parent_id { get; set; }
            public string gateway { get; set; }
            public string source { get; set; }
            public string authorization { get; set; }
        }
    }
}
