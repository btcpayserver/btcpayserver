using System;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class AppDataBase
    {
        public string Id { get; set; }
        public string AppType { get; set; }
        public string Name { get; set; }
        public string StoreId { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset Created { get; set; }
    }
    
    public class PointOfSaleAppData : AppDataBase
    {
        // We can add POS specific things here later
    }

    public class CrowdfundAppData : AppDataBase
    {
        // We can add Crowdfund specific things here later
    }
}
