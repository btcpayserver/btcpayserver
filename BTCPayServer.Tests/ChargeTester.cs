using BTCPayServer.Lightning;
using BTCPayServer.Lightning.Charge;
using NBitcoin;

namespace BTCPayServer.Tests
{
    public class ChargeTester
    {
        private readonly ServerTester _Parent;

        public ChargeTester(ServerTester serverTester, string environmentName, string defaultValue, string defaultHost, Network network)
        {
            this._Parent = serverTester;
            var url = serverTester.GetEnvironment(environmentName, defaultValue);

            Client = (ChargeClient)new LightningClientFactory(network).Create(url);
            P2PHost = _Parent.GetEnvironment(environmentName + "_HOST", defaultHost);
        }
        public ChargeClient Client { get; set; }
        public string P2PHost { get; }
    }
}
