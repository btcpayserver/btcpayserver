using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace BTCPayServer.Tests.Fixtures
{
    public class UserWithBitcoinWalletSelenium : FixtureBase, Xunit.IAsyncLifetime
    {
        public UserWithBitcoinWalletSelenium(IMessageSink messageSink) : base(messageSink)
        {
            SeleniumTester = CreateSeleniumTester();
            ServerTester = SeleniumTester.Server;
        }
        public Task DisposeAsync()
        {
            OutputHelper = null;
            ServerTester.Dispose();
            return Task.CompletedTask;
        }

        public void GoHome()
        {
            SeleniumTester.GoToHome();
        }

        public SeleniumTester SeleniumTester { get; }
        public ServerTester ServerTester { get; set; }

        public async Task InitializeAsync()
        {
            await SeleniumTester.StartAsync();
            SeleniumTester.RegisterNewUser(true);
            SeleniumTester.CreateNewStore();
            SeleniumTester.AddDerivationScheme();
        }
    }
}
