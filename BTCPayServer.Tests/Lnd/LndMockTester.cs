using System;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.LND;
using NBitcoin;

namespace BTCPayServer.Tests.Lnd
{
    public class LndMockTester
    {
        private readonly ServerTester _Parent;

        public LndMockTester(ServerTester serverTester, string environmentName, string defaultValue, string defaultHost, Network network)
        {
            this._Parent = serverTester;
            var url = serverTester.GetEnvironment(environmentName, defaultValue);

            Swagger = new LndSwaggerClient(new LndRestSettings(new Uri(url)) { AllowInsecure = true });
            Client = new LndClient(Swagger, network);
            P2PHost = _Parent.GetEnvironment(environmentName + "_HOST", defaultHost);
        }

        public LndSwaggerClient Swagger { get; set; }
        public ILightningClient Client { get; set; }
        public string P2PHost { get; }
    }
}
