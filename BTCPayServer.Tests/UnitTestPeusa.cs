using BTCPayServer.Payments.Lightning.Lnd;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace BTCPayServer.Tests
{
    // Helper class for testing functionality and generating data needed during coding/debuging
    public class UnitTestPeusa
    {
        // Unit test that generates temorary checkout Bitpay page
        // https://forkbitpay.slack.com/archives/C7M093Z55/p1508293682000217

        // Testnet of Bitpay down
        //[Fact]
        //public void BitpayCheckout()
        //{
        //    var key = new Key(Encoders.Hex.DecodeData("7b70a06f35562873e3dcb46005ed0fe78e1991ad906e56adaaafa40ba861e056"));
        //    var url = new Uri("https://test.bitpay.com/");
        //    var btcpay = new Bitpay(key, url);
        //    var invoice = btcpay.CreateInvoice(new Invoice()
        //    {

        //        Price = 5.0,
        //        Currency = "USD",
        //        PosData = "posData",
        //        OrderId = "cdfd8a5f-6928-4c3b-ba9b-ddf438029e73",
        //        ItemDesc = "Hello from the otherside"
        //    }, Facade.Merchant);

        //    // go to invoice.Url
        //    Console.WriteLine(invoice.Url);
        //}

        // Generating Extended public key to use on http://localhost:14142/stores/{storeId}
        [Fact]
        public void GeneratePubkey()
        {
            var network = Network.RegTest;

            ExtKey masterKey = new ExtKey();
            Console.WriteLine("Master key : " + masterKey.ToString(network));
            ExtPubKey masterPubKey = masterKey.Neuter();

            ExtPubKey pubkey = masterPubKey.Derive(0);
            Console.WriteLine("PubKey " + 0 + " : " + pubkey.ToString(network));
        }
    }
}
