using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning.CLightning
{
    public class NodeInfo
    {
        public NodeInfo(string nodeId, string host, int port)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            if (nodeId == null)
                throw new ArgumentNullException(nameof(nodeId));
            Port = port;
            Host = host;
            NodeId = nodeId;
        }
        public string NodeId { get; private set; }
        public string Host { get; private set; }
        public int Port { get; private set; }
    }
}
