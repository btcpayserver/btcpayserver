using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class CreateCustodianAccountRequest
    {
        public string CustodianCode { get; set; }
        public string Name { get; set; }

        public JObject Config { get; set; }
    }
}
