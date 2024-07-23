using System;
using BTCPayServer.Lightning.CLightning;
using NBitcoin;

namespace BTCPayServer.Tests
{
    public class LightningDTester
    {
        readonly ServerTester parent;
        public LightningDTester(ServerTester parent, string environmentName, string defaultRPC, string defaultHost, Network network)
        {
            this.parent = parent;
            RPC = new CLightningClient(new Uri(parent.GetEnvironment(environmentName, defaultRPC)), network);
        }

        public CLightningClient RPC { get; }
        public string P2PHost { get; }

    }
}
:bc1q4k4zlga72f0t0jrsyh93dzv2k7upry6an304jp