using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace BTCPayServer.Tests.Fixtures
{
    public class UserWithBitcoinWallet : FixtureBase, Xunit.IAsyncLifetime
    {
        public UserWithBitcoinWallet(IMessageSink messageSink) : base(messageSink)
        {
            ServerTester = CreateServerTester();
        }
        public Task DisposeAsync()
        {
            OutputHelper = null;
            ServerTester.Dispose();
            return Task.CompletedTask;
        }

        public ServerTester ServerTester { get; set; }
        public TestAccount TestAccount { get; private set; }

        public async Task InitializeAsync()
        {
            await ServerTester.StartAsync();
            TestAccount = ServerTester.NewAccount();
            await TestAccount.GrantAccessAsync(true);
            await TestAccount.RegisterDerivationSchemeAsync("BTC");
        }
    }
}
