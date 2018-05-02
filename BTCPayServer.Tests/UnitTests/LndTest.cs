using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Payments.Lightning.Lnd;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests.UnitTests
{
    public class LndTest
    {
        private readonly ITestOutputHelper output;

        public LndTest(ITestOutputHelper output)
        {
            this.output = output;
        }

        private LndClient Client
        {
            get
            {
                var lnd = new LndClient(new Uri("http://localhost:53280"), Network.RegTest);
                return lnd;
            }
        }

        [Fact]
        public async Task GetInfo()
        {
            var res = await Client.GetInfo();

            output.WriteLine("Result: " + res.ToJson());
        }

        [Fact]
        public async Task CreateInvoice()
        {
            var res = await Client.CreateInvoice(10000, "Hello world", TimeSpan.FromSeconds(3600));

            output.WriteLine("Result: " + res.ToJson());
        }
    }
}
