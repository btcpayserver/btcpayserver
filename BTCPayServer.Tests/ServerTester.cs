using BTCPayServer.Controllers;
using System.Linq;
using BTCPayServer.Models.AccountViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.RPC;
using NBitpayClient;
using NBXplorer;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Globalization;
using BTCPayServer.Tests.Lnd;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning;

namespace BTCPayServer.Tests
{
    public class ServerTester : IDisposable
    {
        public static ServerTester Create([CallerMemberNameAttribute]string scope = null)
        {
            return new ServerTester(scope);
        }

        string _Directory;
        public ServerTester(string scope)
        {
            _Directory = scope;
            if (Directory.Exists(_Directory))
                Utils.DeleteDirectory(_Directory);
            if (!Directory.Exists(_Directory))
                Directory.CreateDirectory(_Directory);

            NetworkProvider = new BTCPayNetworkProvider(NetworkType.Regtest);
            ExplorerNode = new RPCClient(RPCCredentialString.Parse(GetEnvironment("TESTS_BTCRPCCONNECTION", "server=http://127.0.0.1:43782;ceiwHEbqWI83:DwubwWsoo3")), NetworkProvider.GetNetwork("BTC").NBitcoinNetwork);
            LTCExplorerNode = new RPCClient(RPCCredentialString.Parse(GetEnvironment("TESTS_LTCRPCCONNECTION", "server=http://127.0.0.1:43783;ceiwHEbqWI83:DwubwWsoo3")), NetworkProvider.GetNetwork("LTC").NBitcoinNetwork);

            ExplorerClient = new ExplorerClient(NetworkProvider.GetNetwork("BTC").NBXplorerNetwork, new Uri(GetEnvironment("TESTS_BTCNBXPLORERURL", "http://127.0.0.1:32838/")));
            LTCExplorerClient = new ExplorerClient(NetworkProvider.GetNetwork("LTC").NBXplorerNetwork, new Uri(GetEnvironment("TESTS_LTCNBXPLORERURL", "http://127.0.0.1:32838/")));

            var btc = NetworkProvider.GetNetwork("BTC").NBitcoinNetwork;
            CustomerLightningD = LightningClientFactory.CreateClient(GetEnvironment("TEST_CUSTOMERLIGHTNINGD", "type=clightning;server=tcp://127.0.0.1:30992/"), btc);
            MerchantLightningD = LightningClientFactory.CreateClient(GetEnvironment("TEST_MERCHANTLIGHTNINGD", "type=clightning;server=tcp://127.0.0.1:30993/"), btc);

            MerchantCharge = new ChargeTester(this, "TEST_MERCHANTCHARGE", "type=charge;server=http://127.0.0.1:54938/;api-token=foiewnccewuify", "merchant_lightningd", btc);

            MerchantLnd = new LndMockTester(this, "TEST_MERCHANTLND", "https://lnd:lnd@127.0.0.1:53280/", "merchant_lnd", btc);

            PayTester = new BTCPayServerTester(Path.Combine(_Directory, "pay"))
            {
                NBXplorerUri = ExplorerClient.Address,
                LTCNBXplorerUri = LTCExplorerClient.Address,
                TestDatabase = Enum.Parse<TestDatabases>(GetEnvironment("TESTS_DB", TestDatabases.Postgres.ToString()), true),
                Postgres = GetEnvironment("TESTS_POSTGRES", "User ID=postgres;Host=127.0.0.1;Port=39372;Database=btcpayserver"),
                MySQL = GetEnvironment("TESTS_MYSQL", "User ID=root;Host=127.0.0.1;Port=33036;Database=btcpayserver"),
                IntegratedLightning = MerchantCharge.Client.Uri
            };
            PayTester.Port = int.Parse(GetEnvironment("TESTS_PORT", Utils.FreeTcpPort().ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
            PayTester.HostName = GetEnvironment("TESTS_HOSTNAME", "127.0.0.1");
            PayTester.InContainer = bool.Parse(GetEnvironment("TESTS_INCONTAINER", "false"));
        }

        public bool Dockerized
        {
            get; set;
        }

        public void Start()
        {
            PayTester.Start();
        }

        /// <summary>
        /// Connect a customer LN node to the merchant LN node
        /// </summary>
        /// <returns></returns>
        public Task EnsureChannelsSetup()
        {
            return BTCPayServer.Lightning.Tests.ConnectChannels.ConnectAll(ExplorerNode, GetLightningSenderClients(), GetLightningDestClients());
        }

        private IEnumerable<ILightningClient> GetLightningSenderClients()
        {
            yield return CustomerLightningD;
        }

        private IEnumerable<ILightningClient> GetLightningDestClients()
        {
            yield return MerchantLightningD;
            yield return MerchantLnd.Client;
        }

        public void SendLightningPayment(Invoice invoice)
        {
            SendLightningPaymentAsync(invoice).GetAwaiter().GetResult();
        }

        public async Task SendLightningPaymentAsync(Invoice invoice)
        {
            var bolt11 = invoice.CryptoInfo.Where(o => o.PaymentUrls.BOLT11 != null).First().PaymentUrls.BOLT11;
            bolt11 = bolt11.Replace("lightning:", "", StringComparison.OrdinalIgnoreCase);
            await CustomerLightningD.Pay(bolt11);
        }

        public ILightningClient CustomerLightningD { get; set; }

        public ILightningClient MerchantLightningD { get; private set; }
        public ChargeTester MerchantCharge { get; private set; }
        public LndMockTester MerchantLnd { get; set; }

        internal string GetEnvironment(string variable, string defaultValue)
        {
            var var = Environment.GetEnvironmentVariable(variable);
            return String.IsNullOrEmpty(var) ? defaultValue : var;
        }

        public TestAccount NewAccount()
        {
            return new TestAccount(this);
        }

        public BTCPayNetworkProvider NetworkProvider { get; private set; }
        public RPCClient ExplorerNode
        {
            get; set;
        }

        public RPCClient LTCExplorerNode
        {
            get; set;
        }

        public ExplorerClient ExplorerClient
        {
            get; set;
        }
        public ExplorerClient LTCExplorerClient { get; set; }

        HttpClient _Http = new HttpClient();

        public BTCPayServerTester PayTester
        {
            get; set;
        }
        public List<string> Stores { get; internal set; } = new List<string>();

        public void Dispose()
        {
            foreach (var store in Stores)
            {
                Xunit.Assert.True(PayTester.StoreRepository.DeleteStore(store).GetAwaiter().GetResult());
            }
            if (PayTester != null)
                PayTester.Dispose();
        }
    }
}
