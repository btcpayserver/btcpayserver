using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Eclair;

namespace BTCPayServer.Tests
{
    public class EclairTester
    {
        ServerTester parent;
        public EclairTester(ServerTester parent, string environmentName, string defaultRPC, string defaultHost)
        {
            this.parent = parent;
            RPC = new EclairRPCClient(new Uri(parent.GetEnvironment(environmentName, defaultRPC)), parent.Network);
            P2PHost = parent.GetEnvironment(environmentName + "_HOST", defaultHost);
        }

        public EclairRPCClient RPC { get; }
        public string P2PHost { get; }

        NodeInfo _NodeInfo;
        public async Task<NodeInfo> GetNodeInfoAsync()
        {
            if (_NodeInfo != null)
                return _NodeInfo;
            var info = await RPC.GetInfoAsync();
            _NodeInfo = new NodeInfo(info.NodeId, P2PHost, info.Port);
            return _NodeInfo;
        }

        public NodeInfo GetNodeInfo()
        {
            return GetNodeInfoAsync().GetAwaiter().GetResult();
        }
    }
}
