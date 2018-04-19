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
using BTCPayServer.Payments.Lightning.CLightning;
using BTCPayServer.Payments.Lightning.Charge;

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

            NetworkProvider = new BTCPayNetworkProvider(ChainType.Regtest);
            ExplorerNode = new RPCClient(RPCCredentialString.Parse(GetEnvironment("TESTS_BTCRPCCONNECTION", "server=http://127.0.0.1:43782;ceiwHEbqWI83:DwubwWsoo3")), NetworkProvider.GetNetwork("BTC").NBitcoinNetwork);
            LTCExplorerNode = new RPCClient(RPCCredentialString.Parse(GetEnvironment("TESTS_LTCRPCCONNECTION", "server=http://127.0.0.1:43783;ceiwHEbqWI83:DwubwWsoo3")), NetworkProvider.GetNetwork("LTC").NBitcoinNetwork);

            ExplorerClient = new ExplorerClient(NetworkProvider.GetNetwork("BTC").NBXplorerNetwork, new Uri(GetEnvironment("TESTS_BTCNBXPLORERURL", "http://127.0.0.1:32838/")));
            LTCExplorerClient = new ExplorerClient(NetworkProvider.GetNetwork("LTC").NBXplorerNetwork, new Uri(GetEnvironment("TESTS_LTCNBXPLORERURL", "http://127.0.0.1:32838/")));

            var btc = NetworkProvider.GetNetwork("BTC").NBitcoinNetwork;
            CustomerLightningD = new CLightningRPCClient(new Uri(GetEnvironment("TEST_CUSTOMERLIGHTNINGD", "tcp://127.0.0.1:30992/")), btc);
            MerchantLightningD = new CLightningRPCClient(new Uri(GetEnvironment("TEST_MERCHANTLIGHTNINGD", "tcp://127.0.0.1:30993/")), btc);

            MerchantCharge = new ChargeTester(this, "TEST_MERCHANTCHARGE", "http://api-token:foiewnccewuify@127.0.0.1:54938/", "merchant_lightningd", btc);

            PayTester = new BTCPayServerTester(Path.Combine(_Directory, "pay"))
            {
                NBXplorerUri = ExplorerClient.Address,
                LTCNBXplorerUri = LTCExplorerClient.Address,
                Postgres = GetEnvironment("TESTS_POSTGRES", "User ID=postgres;Host=127.0.0.1;Port=39372;Database=btcpayserver"),
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
        public void PrepareLightning()
        {
            PrepareLightningAsync().GetAwaiter().GetResult();
        }


        /// <summary>
        /// Connect a customer LN node to the merchant LN node
        /// </summary>
        /// <returns></returns>
        public async Task PrepareLightningAsync()
        {
            while (true)
            {
                var skippedStates = new[] { "ONCHAIN", "CHANNELD_SHUTTING_DOWN", "CLOSINGD_SIGEXCHANGE", "CLOSINGD_COMPLETE", "FUNDING_SPEND_SEEN" };
                var channel = (await CustomerLightningD.ListPeersAsync())
                            .SelectMany(p => p.Channels)
                            .Where(c => !skippedStates.Contains(c.State ?? ""))
                            .FirstOrDefault();
                switch (channel?.State)
                {
                    case null:
                        var merchantInfo = await WaitLNSynched();
                        var clightning = new NodeInfo(merchantInfo.Id, MerchantCharge.P2PHost, merchantInfo.Port);
                        await CustomerLightningD.ConnectAsync(clightning);
                        var address = await CustomerLightningD.NewAddressAsync();
                        await ExplorerNode.SendToAddressAsync(address, Money.Coins(0.2m));
                        ExplorerNode.Generate(1);
                        await WaitLNSynched();
                        await Task.Delay(1000);
                        await CustomerLightningD.FundChannelAsync(clightning, Money.Satoshis(16777215));
                        break;
                    case "CHANNELD_AWAITING_LOCKIN":
                        ExplorerNode.Generate(1);
                        await WaitLNSynched();
                        break;
                    case "CHANNELD_NORMAL":
                        return;
                    default:
                        throw new NotSupportedException(channel?.State ?? "");
                }
            }
        }

        private async Task<GetInfoResponse> WaitLNSynched()
        {
            while (true)
            {
                var merchantInfo = await MerchantCharge.Client.GetInfoAsync();
                var blockCount = await ExplorerNode.GetBlockCountAsync();
                if (merchantInfo.BlockHeight != blockCount)
                {
                    await Task.Delay(1000);
                }
                else
                {
                    return merchantInfo;
                }
            }
        }

        public void SendLightningPayment(Invoice invoice)
        {
            SendLightningPaymentAsync(invoice).GetAwaiter().GetResult();
        }

        public async Task SendLightningPaymentAsync(Invoice invoice)
        {
            var bolt11 = invoice.CryptoInfo.Where(o => o.PaymentUrls.BOLT11 != null).First().PaymentUrls.BOLT11;
            bolt11 = bolt11.Replace("lightning:", "", StringComparison.OrdinalIgnoreCase);
            await CustomerLightningD.SendAsync(bolt11);
        }

        public CLightningRPCClient CustomerLightningD { get; set; }
        public CLightningRPCClient MerchantLightningD { get; private set; }
        public ChargeTester MerchantCharge { get; private set; }

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

        class MockHttpRequest : HttpRequest
        {
            Uri serverUri;
            public MockHttpRequest(Uri serverUri)
            {
                this.serverUri = serverUri;
            }
            public override HttpContext HttpContext => throw new NotImplementedException();

            public override string Method
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }
            public override string Scheme
            {
                get => serverUri.Scheme;
                set => throw new NotImplementedException();
            }
            public override bool IsHttps
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }
            public override HostString Host
            {
                get => new HostString(serverUri.Host, serverUri.Port);
                set => throw new NotImplementedException();
            }
            public override PathString PathBase
            {
                get => "";
                set => throw new NotImplementedException();
            }
            public override PathString Path
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }
            public override QueryString QueryString
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }
            public override IQueryCollection Query
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }
            public override string Protocol
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }

            public override IHeaderDictionary Headers => throw new NotImplementedException();

            public override IRequestCookieCollection Cookies
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }
            public override long? ContentLength
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }
            public override string ContentType
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }
            public override Stream Body
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }

            public override bool HasFormContentType => throw new NotImplementedException();

            public override IFormCollection Form
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }

            public override Task<IFormCollection> ReadFormAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                throw new NotImplementedException();
            }
        }


        public BTCPayServerTester PayTester
        {
            get; set;
        }

        public void Dispose()
        {
            if (PayTester != null)
                PayTester.Dispose();
        }
    }
}
