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
using BTCPayServer.Eclair;

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
        }

        public bool Dockerized
        {
            get; set;
        }

        public void Start()
        {
            if (Directory.Exists(_Directory))
                Utils.DeleteDirectory(_Directory);
            if (!Directory.Exists(_Directory))
                Directory.CreateDirectory(_Directory);


            FakeCallback = bool.Parse(GetEnvironment("TESTS_FAKECALLBACK", "true"));
            ExplorerNode = new RPCClient(RPCCredentialString.Parse(GetEnvironment("TESTS_RPCCONNECTION", "server=http://127.0.0.1:43782;ceiwHEbqWI83:DwubwWsoo3")), Network);
            ExplorerClient = new ExplorerClient(Network, new Uri(GetEnvironment("TESTS_NBXPLORERURL", "http://127.0.0.1:32838/")));
            PayTester = new BTCPayServerTester(Path.Combine(_Directory, "pay"))
            {
                NBXplorerUri = ExplorerClient.Address,
                Postgres = GetEnvironment("TESTS_POSTGRES", "User ID=postgres;Host=127.0.0.1;Port=39372;Database=btcpayserver")
            };
            PayTester.Port = int.Parse(GetEnvironment("TESTS_PORT", Utils.FreeTcpPort().ToString()));
            PayTester.HostName = GetEnvironment("TESTS_HOSTNAME", "127.0.0.1");
            PayTester.Start();

            MerchantEclair = new EclairTester(this, "TEST_ECLAIR1", "http://127.0.0.1:30992/", "eclair1");
            CustomerEclair = new EclairTester(this, "TEST_ECLAIR2", "http://127.0.0.1:30993/", "eclair2");
        }


        /// <summary>
        /// This will setup a channel going from customer to merchant
        /// </summary>
        public void PrepareLightning()
        {
            PrepareLightningAsync().GetAwaiter().GetResult();
        }

        public async Task PrepareLightningAsync()
        {
            // Activate segwit
            var blockCount = ExplorerNode.GetBlockCountAsync();
            // Fetch node info, but that in cache
            var merchant = MerchantEclair.GetNodeInfoAsync();
            var customer = CustomerEclair.GetNodeInfoAsync();
            var channels = CustomerEclair.RPC.ChannelsAsync();
            var connect = CustomerEclair.RPC.ConnectAsync(merchant.Result);
            await Task.WhenAll(blockCount, merchant, customer, channels, connect);

            // Mine until segwit is activated
            if (blockCount.Result <= 432)
            {
                ExplorerNode.Generate(433 - blockCount.Result);
            }
        }

        public EclairTester MerchantEclair { get; set; }
        public EclairTester CustomerEclair { get; set; }

        internal string GetEnvironment(string variable, string defaultValue)
        {
            var var = Environment.GetEnvironmentVariable(variable);
            return String.IsNullOrEmpty(var) ? defaultValue : var;
        }

        public TestAccount NewAccount()
        {
            return new TestAccount(this);
        }

        public bool FakeCallback
        {
            get;
            set;
        }
        public RPCClient ExplorerNode
        {
            get; set;
        }

        public ExplorerClient ExplorerClient
        {
            get; set;
        }

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

        /// <summary>
        /// Simulating callback from NBXplorer. NBXplorer can't reach the host during tests as it is not running on localhost.
        /// </summary>
        /// <param name="address"></param>
        public void SimulateCallback(BitcoinAddress address = null)
        {
            if (!FakeCallback) //The callback of NBXplorer should work
                return;

            var req = new MockHttpRequest(PayTester.ServerUri);
            var controller = PayTester.GetController<CallbackController>();
            if (address != null)
            {

                var match = new TransactionMatch();
                match.Outputs.Add(new KeyPathInformation() { ScriptPubKey = address.ScriptPubKey });
                var content = new StringContent(new NBXplorer.Serializer(Network).ToString(match), new UTF8Encoding(false), "application/json");
                var uri = controller.GetCallbackUriAsync(req).GetAwaiter().GetResult();

                HttpRequestMessage message = new HttpRequestMessage();
                message.Method = HttpMethod.Post;
                message.RequestUri = uri;
                message.Content = content;

                _Http.SendAsync(message).GetAwaiter().GetResult();
            }
            else
            {

                var uri = controller.GetCallbackBlockUriAsync(req).GetAwaiter().GetResult();
                HttpRequestMessage message = new HttpRequestMessage();
                message.Method = HttpMethod.Post;
                message.RequestUri = uri;
                _Http.SendAsync(message).GetAwaiter().GetResult();
            }
        }


        public BTCPayServerTester PayTester
        {
            get; set;
        }

        public Network Network
        {
            get;
            set;
        } = Network.RegTest;

        public void Dispose()
        {
            if (PayTester != null)
                PayTester.Dispose();
        }
    }
}
