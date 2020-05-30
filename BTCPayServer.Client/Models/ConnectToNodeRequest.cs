using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class ConnectToNodeRequest
    {
        [JsonProperty(PropertyName = "nodeURI")]
        [JsonConverter(typeof(NodeInfoJsonConverter))]
        public NodeInfo NodeURI { get; set; }
    }
}
