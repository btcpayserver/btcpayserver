using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Hosting;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Tests.Lnd;
using BTCPayServer.Tests.Logging;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;
using NBitpayClient;
using NBXplorer;

namespace BTCPayServer.Tests
{
    public class ServerTester : IDisposable
    {
        public List<IDisposable> Resources = new List<IDisposable>();
        readonly string _Directory;

        public ILoggerProvider LoggerProvider { get; }

        internal ILog TestLogs;
        public ServerTester(string scope, bool newDb, ILog testLogs, ILoggerProvider loggerProvider, BTCPayNetworkProvider networkProvider)
        {
            LoggerProvider = loggerProvider;
            this.TestLogs = testLogs;
            _Directory = scope;
            if (Directory.Exists(_Directory))
                Utils.DeleteDirectory(_Directory);
            if (!Directory.Exists(_Directory))
                Directory.CreateDirectory(_Directory);

            NetworkProvider = networkProvider;
            ExplorerNode = new RPCClient(RPCCredentialString.Parse(GetEnvironment("TESTS_BTCRPCCONNECTION", "server=http://127.0.0.1:43782;ceiwHEbqWI83:DwubwWsoo3")), NetworkProvider.GetNetwork<BTCPayNetwork>("BTC").NBitcoinNetwork);
            ExplorerNode.ScanRPCCapabilities();

            ExplorerClient = new ExplorerClient(NetworkProvider.GetNetwork<BTCPayNetwork>("BTC").NBXplorerNetwork, new Uri(GetEnvironment("TESTS_BTCNBXPLORERURL", "http://127.0.0.1:32838/")));

            PayTester = new BTCPayServerTester(TestLogs, LoggerProvider, Path.Combine(_Directory, "pay"))
            {
                NBXplorerUri = ExplorerClient.Address,
                TestDatabase = Enum.Parse<TestDatabases>(GetEnvironment("TESTS_DB", TestDatabases.Postgres.ToString()), true),
                // TODO: The fact that we use same conn string as development database can cause huge problems with tests
                // since in dev we already can have some users / stores registered, while on CI database is being initalized
                // for the first time and first registered user gets admin status by default
                Postgres = GetEnvironment("TESTS_POSTGRES", "User ID=postgres;Include Error Detail=true;Host=127.0.0.1;Port=39372;Database=btcpayserver"),
                ExplorerPostgres = GetEnvironment("TESTS_EXPLORER_POSTGRES", "User ID=postgres;Include Error Detail=true;Host=127.0.0.1;Port=39372;Database=nbxplorer"),
                MySQL = GetEnvironment("TESTS_MYSQL", "User ID=root;Host=127.0.0.1;Port=33036;Database=btcpayserver")
            };
            if (newDb)
            {
                var r = RandomUtils.GetUInt32();
                PayTester.Postgres = PayTester.Postgres.Replace("btcpayserver", $"btcpayserver{r}");
                PayTester.MySQL = PayTester.MySQL.Replace("btcpayserver", $"btcpayserver{r}");
                TestLogs.LogInformation($"Database used: btcpayserver{r}");
            }
            PayTester.Port = int.Parse(GetEnvironment("TESTS_PORT", Utils.FreeTcpPort().ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
            PayTester.HostName = GetEnvironment("TESTS_HOSTNAME", "127.0.0.1");
            PayTester.InContainer = bool.Parse(GetEnvironment("TESTS_INCONTAINER", "false"));

            PayTester.SSHPassword = GetEnvironment("TESTS_SSHPASSWORD", "opD3i2282D");
            PayTester.SSHKeyFile = GetEnvironment("TESTS_SSHKEYFILE", "");
            PayTester.SSHConnection = GetEnvironment("TESTS_SSHCONNECTION", "root@127.0.0.1:21622");
            PayTester.SocksEndpoint = GetEnvironment("TESTS_SOCKSENDPOINT", "localhost:9050");
        }
#if ALTCOINS
        public void ActivateLTC()
        {
            LTCExplorerNode = new RPCClient(RPCCredentialString.Parse(GetEnvironment("TESTS_LTCRPCCONNECTION", "server=http://127.0.0.1:43783;ceiwHEbqWI83:DwubwWsoo3")), NetworkProvider.GetNetwork<BTCPayNetwork>("LTC").NBitcoinNetwork);
            LTCExplorerClient = new ExplorerClient(NetworkProvider.GetNetwork<BTCPayNetwork>("LTC").NBXplorerNetwork, new Uri(GetEnvironment("TESTS_LTCNBXPLORERURL", "http://127.0.0.1:32838/")));
            PayTester.Chains.Add("LTC");
            PayTester.LTCNBXplorerUri = LTCExplorerClient.Address;
        }
        public void ActivateLBTC()
        {
            LBTCExplorerNode = new RPCClient(RPCCredentialString.Parse(GetEnvironment("TESTS_LBTCRPCCONNECTION", "server=http://127.0.0.1:19332;liquid:liquid")), NetworkProvider.GetNetwork<BTCPayNetwork>("LBTC").NBitcoinNetwork);
            LBTCExplorerClient = new ExplorerClient(NetworkProvider.GetNetwork<BTCPayNetwork>("LBTC").NBXplorerNetwork, new Uri(GetEnvironment("TESTS_LBTCNBXPLORERURL", "http://127.0.0.1:32838/")));
            PayTester.Chains.Add("LBTC");
            PayTester.LBTCNBXplorerUri = LBTCExplorerClient.Address;
        }
        public void ActivateETH()
        {
            PayTester.Chains.Add("ETH");
        }

#endif
        public void ActivateLightning()
        {
            ActivateLightning(LightningConnectionType.CLightning);
        }
        public void ActivateLightning(string internalNode)
        {
            var btc = NetworkProvider.GetNetwork<BTCPayNetwork>("BTC").NBitcoinNetwork;
            var factory = new LightningClientFactory(btc);
            CustomerLightningD = factory.Create(GetEnvironment("TEST_CUSTOMERLIGHTNINGD", "type=clightning;server=tcp://127.0.0.1:30992/"));
            MerchantLightningD = factory.Create(GetEnvironment("TEST_MERCHANTLIGHTNINGD", "type=clightning;server=tcp://127.0.0.1:30993/"));
            MerchantCharge = new ChargeTester(this, "TEST_MERCHANTCHARGE", "type=charge;server=http://127.0.0.1:54938/;api-token=foiewnccewuify;allowinsecure=true", "merchant_lightningd", btc);
            MerchantLnd = new LndMockTester(this, "TEST_MERCHANTLND", "http://lnd:lnd@127.0.0.1:35531/", "merchant_lnd", btc);
            PayTester.UseLightning = true;
            PayTester.IntegratedLightning = GetLightningConnectionString(internalNode, true);
        }
        public string GetLightningConnectionString(string connectionType, bool isMerchant)
        {
            string connectionString = null;
            if (connectionType is null)
                return LightningSupportedPaymentMethod.InternalNode;
            if (connectionType == LightningConnectionType.CLightning)
            {
                if (isMerchant)
                    connectionString = "type=clightning;server=" +
                                       ((CLightningClient)MerchantLightningD).Address.AbsoluteUri;
                else
                    connectionString = "type=clightning;server=" +
                                   ((CLightningClient)CustomerLightningD).Address.AbsoluteUri;
            }
            else if (connectionType == LightningConnectionType.LndREST)
            {
                if (isMerchant)
                    connectionString = $"type=lnd-rest;server={MerchantLnd.Swagger.BaseUrl};allowinsecure=true";
                else
                    throw new NotSupportedException();
            }
            else
                throw new NotSupportedException(connectionType.ToString());
            return connectionString;
        }

        public bool Dockerized
        {
            get; set;
        }

        public Task StartAsync()
        {
            return PayTester.StartAsync();
        }

        /// <summary>
        /// Connect a customer LN node to the merchant LN node
        /// </summary>
        /// <returns></returns>
        public async Task EnsureChannelsSetup()
        {
            TestLogs.LogInformation("Connecting channels");
            BTCPayServer.Lightning.Tests.ConnectChannels.Logs = LoggerProvider.CreateLogger("Connect channels");
            await BTCPayServer.Lightning.Tests.ConnectChannels.ConnectAll(ExplorerNode, GetLightningSenderClients(), GetLightningDestClients()).ConfigureAwait(false);
            TestLogs.LogInformation("Channels connected");
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

        public async Task<PayResponse> SendLightningPaymentAsync(Invoice invoice)
        {
            var bolt11 = invoice.CryptoInfo.Where(o => o.PaymentUrls?.BOLT11 != null).First().PaymentUrls.BOLT11;
            bolt11 = bolt11.Replace("lightning:", "", StringComparison.OrdinalIgnoreCase);
            return await CustomerLightningD.Pay(bolt11);
        }

        public async Task<T> WaitForEvent<T>(Func<Task> action, Func<T, bool> correctEvent = null)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sub = PayTester.GetService<EventAggregator>().Subscribe<T>(evt =>
            {
                if (correctEvent is null)
                    tcs.TrySetResult(evt);
                else if (correctEvent(evt))
                {
                    tcs.TrySetResult(evt);
                }
            });
            if (action != null)
                await action.Invoke();
            var result = await tcs.Task;
            sub.Dispose();
            return result;
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
#if ALTCOINS
        public RPCClient LTCExplorerNode
        {
            get; set;
        }

        public RPCClient LBTCExplorerNode { get; set; }
        public ExplorerClient LTCExplorerClient { get; set; }
        public ExplorerClient LBTCExplorerClient { get; set; }
#endif

        public ExplorerClient ExplorerClient
        {
            get; set;
        }

        readonly HttpClient _Http = new HttpClient();

        public BTCPayServerTester PayTester
        {
            get; set;
        }

        public List<string> Stores { get; internal set; } = new List<string>();
        public bool DeleteStore { get; set; } = true;
        public BTCPayNetworkBase DefaultNetwork => NetworkProvider.DefaultNetwork;

        public void Dispose()
        {
            foreach (var r in this.Resources)
                r.Dispose();
            TestLogs.LogInformation("Disposing the BTCPayTester...");
            if (DeleteStore)
            {
                foreach (var store in Stores)
                {
                    Xunit.Assert.True(PayTester.StoreRepository.DeleteStore(store).GetAwaiter().GetResult());
                }
            }
            if (PayTester != null)
                PayTester.Dispose();
            TestLogs.LogInformation("BTCPayTester disposed");
        }
    }
}
