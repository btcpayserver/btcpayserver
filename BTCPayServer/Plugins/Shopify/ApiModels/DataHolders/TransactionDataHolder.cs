using System;

namespace BTCPayServer.Plugins.Shopify.ApiModels.DataHolders
{
    public class TransactionDataHolder
    {
        public long id { get; set; }
        public long? order_id { get; set; }
        public string kind { get; set; }
        public string gateway { get; set; }
        public string status { get; set; }
        public string message { get; set; }
        public DateTimeOffset created_at { get; set; }
        public bool test { get; set; }
        public string authorization { get; set; }
        public string location_id { get; set; }
        public string user_id { get; set; }
        public long? parent_id { get; set; }
        public DateTimeOffset processed_at { get; set; }
        public string device_id { get; set; }
        public object receipt { get; set; }
        public string error_code { get; set; }
        public string source_name { get; set; }
        public string currency_exchange_adjustment { get; set; }
        public string amount { get; set; }
        public string currency { get; set; }
        public string admin_graphql_api_id { get; set; }
    }
}
