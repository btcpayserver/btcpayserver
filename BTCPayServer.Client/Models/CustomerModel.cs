using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class CustomerModel
{
    public string StoreId { get; set; }
    public string Id { get; set; }
    public string ExternalId { get; set; }
    public JObject Identities { get; set; }
    public JObject Metadata { get; set; }
}
