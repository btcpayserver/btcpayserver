using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payments.Lightning.Lnd;
using NBitcoin;
using NBitcoin.RPC;
using Xunit;
using Xunit.Abstractions;
using System.Linq;
using System.Threading;

namespace BTCPayServer.Tests.UnitTests
{
    // this depends for now on `docker-compose up devlnd`
    public class LndTest
    {
        private readonly ITestOutputHelper output;

        public LndTest(ITestOutputHelper output)
        {
            this.output = output;
            initializeEnvironment();

            MerchantLnd = LndSwaggerClientCustomHttp.Create(new Uri("http://127.0.0.1:53280"), Network.RegTest);
            InvoiceClient = new LndInvoiceClient(MerchantLnd);

            CustomerLnd = LndSwaggerClientCustomHttp.Create(new Uri("http://127.0.0.1:53281"), Network.RegTest);
        }

        private LndSwaggerClientCustomHttp MerchantLnd { get; set; }
        private LndInvoiceClient InvoiceClient { get; set; }

        private LndSwaggerClientCustomHttp CustomerLnd { get; set; }

        [Fact]
        public async Task GetInfo()
        {
            var res = await InvoiceClient.GetInfo();
            output.WriteLine("Result: " + res.ToJson());
        }

        [Fact]
        public async Task CreateInvoice()
        {
            var res = await InvoiceClient.CreateInvoice(10000, "Hello world", TimeSpan.FromSeconds(3600));
            output.WriteLine("Result: " + res.ToJson());
        }

        [Fact]
        public async Task GetInvoice()
        {
            var createInvoice = await InvoiceClient.CreateInvoice(10000, "Hello world", TimeSpan.FromSeconds(3600));
            var getInvoice = await InvoiceClient.GetInvoice(createInvoice.Id);

            Assert.Equal(createInvoice.BOLT11, getInvoice.BOLT11);
        }



        [Fact]
        public async Task CreateLndInvoiceAndPay()
        {
            var merchantInvoice = await InvoiceClient.CreateInvoice(10000, "Hello world", TimeSpan.FromSeconds(3600));

            await EnsureLightningChannelAsync();
            var payResponse = await CustomerLnd.SendPaymentSyncAsync(new LnrpcSendRequest
            {
                Payment_request = merchantInvoice.BOLT11
            });

            var invoice = await InvoiceClient.GetInvoice(merchantInvoice.Id);

            Assert.True(invoice.PaidAt.HasValue);
        }


        public async Task<LnrpcChannel> EnsureLightningChannelAsync()
        {
            var merchantInfo = await WaitLNSynched();
            var merchantNodeAddress = new LnrpcLightningAddress
            {
                Pubkey = merchantInfo.NodeId,
                Host = "merchant_lnd:9735"
            };

            while (true)
            {
                // if channel is pending generate blocks until confirmed
                var pendingResponse = await CustomerLnd.PendingChannelsAsync();
                if (pendingResponse.Pending_open_channels?
                    .Any(a => a.Channel?.Remote_node_pub == merchantNodeAddress.Pubkey) == true)
                {
                    ExplorerNode.Generate(1);
                    await WaitLNSynched();
                    continue;
                }

                // check if channel is established
                var chanResponse = await CustomerLnd.ListChannelsAsync(null, null, null, null);
                LnrpcChannel channelToMerchant = null;
                if (chanResponse != null && chanResponse.Channels != null)
                {
                    channelToMerchant = chanResponse.Channels
                    .Where(a => a.Remote_pubkey == merchantNodeAddress.Pubkey)
                    .FirstOrDefault();
                }

                if (channelToMerchant == null)
                {
                    // create new channel
                    var isConnected = await CustomerLnd.ListPeersAsync();
                    if (isConnected.Peers == null ||
                        !isConnected.Peers.Any(a => a.Pub_key == merchantInfo.NodeId))
                    {
                        var connectResp = await CustomerLnd.ConnectPeerAsync(new LnrpcConnectPeerRequest
                        {
                            Addr = merchantNodeAddress
                        });
                    }

                    var addressResponse = await CustomerLnd.NewWitnessAddressAsync();
                    var address = BitcoinAddress.Create(addressResponse.Address, Network.RegTest);
                    await ExplorerNode.SendToAddressAsync(address, Money.Coins(0.2m));
                    ExplorerNode.Generate(1);
                    await WaitLNSynched();

                    var channelReq = new LnrpcOpenChannelRequest
                    {
                        Local_funding_amount = 16777215.ToString(),
                        Node_pubkey_string = merchantInfo.NodeId
                    };
                    var channelResp = await CustomerLnd.OpenChannelSyncAsync(channelReq);
                }
                else
                {
                    // channel exists, return it
                    ExplorerNode.Generate(1);
                    await WaitLNSynched();
                    return channelToMerchant;
                }
            }
        }

        private async Task<LightningNodeInformation> WaitLNSynched()
        {
            while (true)
            {
                var merchantInfo = await InvoiceClient.GetInfo();
                var blockCount = await ExplorerNode.GetBlockCountAsync();
                if (merchantInfo.BlockHeight != blockCount)
                {
                    await Task.Delay(500);
                }
                else
                {
                    return merchantInfo;
                }
            }
        }




        //
        private void initializeEnvironment()
        {
            NetworkProvider = new BTCPayNetworkProvider(NetworkType.Regtest);
            ExplorerNode = new RPCClient(RPCCredentialString.Parse(GetEnvironment("TESTS_BTCRPCCONNECTION", "server=http://127.0.0.1:43782;ceiwHEbqWI83:DwubwWsoo3")), NetworkProvider.GetNetwork("BTC").NBitcoinNetwork);
        }

        public BTCPayNetworkProvider NetworkProvider { get; private set; }
        public RPCClient ExplorerNode { get; set; }

        internal string GetEnvironment(string variable, string defaultValue)
        {
            var var = Environment.GetEnvironmentVariable(variable);
            return String.IsNullOrEmpty(var) ? defaultValue : var;
        }
    }
}
