using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class ConnectToNodeRequest
    {
        public ConnectToNodeRequest()
        {

        }
        public ConnectToNodeRequest(NodeInfo nodeInfo)
        {
            NodeURI = nodeInfo;
        }
        [JsonConverter(typeof(NodeUriJsonConverter))]
        [JsonProperty("nodeURI")]
        public NodeInfo NodeURI { get; set; }
    }
}
