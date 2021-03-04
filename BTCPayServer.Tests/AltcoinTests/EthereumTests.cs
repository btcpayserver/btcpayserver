using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Tests.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using NBitcoin;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class EthereumTests
    {
        public const int TestTimeout = 60_000;

        public EthereumTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        [Trait("Altcoins", "Altcoins")]
        public void LoadSubChainsAlways()
        {
            var config = new ConfigurationRoot(new List<IConfigurationProvider>()
            {
                new MemoryConfigurationProvider(new MemoryConfigurationSource()
                {
                    InitialData = new[] {new KeyValuePair<string, string>("chains", "usdt20"),}
                })
            });

            var networkProvider = config.ConfigureNetworkProvider();
            Assert.NotNull(networkProvider.GetNetwork("ETH"));
            Assert.NotNull(networkProvider.GetNetwork("USDT20"));
        }

        [Fact]
        [Trait("Altcoins", "Altcoins")]
        public async Task CanUseEthereum()
        {
            using var s = SeleniumTester.Create("ETHEREUM", true);
            s.Server.ActivateETH();
            await s.StartAsync();
            s.RegisterNewUser(true);

            IWebElement syncSummary = null;
            TestUtils.Eventually(() =>
            {
                syncSummary = s.Driver.FindElement(By.Id("modalDialog"));
                Assert.True(syncSummary.Displayed);
            });
            var web3Link = syncSummary.FindElement(By.LinkText("Configure Web3"));
            web3Link.Click();
            s.Driver.FindElement(By.Id("Web3ProviderUrl")).SendKeys("https://ropsten-rpc.linkpool.io");
            s.Driver.FindElement(By.Id("saveButton")).Click();
            s.FindAlertMessage();
            TestUtils.Eventually(() =>
            {
                s.Driver.Navigate().Refresh();
                s.Driver.AssertElementNotFound(By.Id("modalDialog"));
            });

            var store = s.CreateNewStore();
            s.Driver.FindElement(By.LinkText("Ethereum")).Click();

            var seed = new Mnemonic(Wordlist.English);
            s.Driver.FindElement(By.Id("ModifyETH")).Click();
            s.Driver.FindElement(By.Id("Seed")).SendKeys(seed.ToString());
            s.Driver.SetCheckbox(By.Id("StoreSeed"), true);
            s.Driver.SetCheckbox(By.Id("Enabled"), true);
            s.Driver.FindElement(By.Id("SaveButton")).Click();
            s.FindAlertMessage();
            s.Driver.FindElement(By.Id("ModifyUSDT20")).Click();
            s.Driver.FindElement(By.Id("Seed")).SendKeys(seed.ToString());
            s.Driver.SetCheckbox(By.Id("StoreSeed"), true);
            s.Driver.SetCheckbox(By.Id("Enabled"), true);
            s.Driver.FindElement(By.Id("SaveButton")).Click();
            s.FindAlertMessage();

            var invoiceId = s.CreateInvoice(store.storeName, 10);
            s.GoToInvoiceCheckout(invoiceId);
            var currencyDropdownButton = s.Driver.FindElement(By.ClassName("payment__currencies"));
            Assert.Contains("ETH", currencyDropdownButton.Text);
            s.Driver.FindElement(By.Id("copy-tab")).Click();

            var ethAddress = s.Driver.FindElements(By.ClassName("copySectionBox"))
                .Single(element => element.FindElement(By.TagName("label")).Text
                    .Contains("Address", StringComparison.InvariantCultureIgnoreCase)).FindElement(By.TagName("input"))
                .GetAttribute("value");
            currencyDropdownButton.Click();
            var elements = s.Driver.FindElement(By.ClassName("vex-content")).FindElements(By.ClassName("vexmenuitem"));
            Assert.Equal(2, elements.Count);

            elements.Single(element => element.Text.Contains("USDT20")).Click();
            s.Driver.FindElement(By.Id("copy-tab")).Click();
            var usdtAddress = s.Driver.FindElements(By.ClassName("copySectionBox"))
                .Single(element => element.FindElement(By.TagName("label")).Text
                    .Contains("Address", StringComparison.InvariantCultureIgnoreCase)).FindElement(By.TagName("input"))
                .GetAttribute("value");
            Assert.Equal(usdtAddress, ethAddress);
        }
    }
}
