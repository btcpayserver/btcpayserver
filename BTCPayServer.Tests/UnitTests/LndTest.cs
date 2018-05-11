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
            InvoiceClient = new LndClient(MerchantLnd);

            CustomerLnd = LndSwaggerClientCustomHttp.Create(new Uri("http://127.0.0.1:53281"), Network.RegTest);
        }

        private LndSwaggerClientCustomHttp MerchantLnd { get; set; }
        private LndClient InvoiceClient { get; set; }

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
        public async Task SetupWalletForPayment()
        {
            var merchantNodeInfo = await InvoiceClient.GetInfo();
            var addressResponse = await CustomerLnd.NewWitnessAddressAsync();
            var address = BitcoinAddress.Create(addressResponse.Address, Network.RegTest);
            await ExplorerNode.SendToAddressAsync(address, Money.Coins(0.2m));
            ExplorerNode.Generate(1);
            await WaitLNSynched();
            await Task.Delay(1000);

            var connectResp = await CustomerLnd.ConnectPeerAsync(new LnrpcConnectPeerRequest
            {
                Addr = new LnrpcLightningAddress
                {
                    Pubkey = merchantNodeInfo.NodeId,
                    Host = "merchant_lnd:8080"
                }
            });

            // We need two instances of lnd... one for merchant, one for buyer
            // prepare that in next commit
            //var channelReq = new LnrpcOpenChannelRequest
            //{
            //    Local_funding_amount = 16777215.ToString()
            //};
            //var channelResp = await LndRpc.OpenChannelSyncAsync(channelReq);

            output.WriteLine("Wallet Address: " + address);
        }

        private async Task<LightningNodeInformation> WaitLNSynched()
        {
            while (true)
            {
                var merchantInfo = await InvoiceClient.GetInfo();
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
