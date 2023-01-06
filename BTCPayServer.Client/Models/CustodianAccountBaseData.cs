using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public abstract class CustodianAccountBaseData
    {
        public string CustodianCode { get; set; }

        public string Name { get; set; }

        public string StoreId { get; set; }

        public JObject Config { get; set; }
    }

}
