using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using NBitcoin;
using NBitpayClient;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class LNbankTests : UnitTestBase
    {
        private const int TestTimeout = TestUtils.TestTimeout;

        public LNbankTests(ITestOutputHelper helper) : base(helper)
        {
        }
        
        [Fact(Timeout = TestTimeout)]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLNbank()
        {
            using var s = CreateSeleniumTester();
            s.Server.ActivateLightning();
            await s.StartAsync();
            s.RegisterNewUser(true);
            
            // Check plugin
            s.Driver.FindElement(By.Id("Nav-AddPlugin")).Click();
            Assert.Contains("id=\"BTCPayServer.Plugins.LNbank\"", s.Driver.PageSource);
            
            // Setup store LN node with LNbank
            s.CreateNewStore();
            s.Driver.FindElement(By.Id("StoreNav-LightningBTC")).Click();
            s.Driver.FindElement(By.CssSelector("label[for=\"LightningNodeType-LNbank\"]")).Click();
            s.Driver.WaitForElement(By.Id("LNbank-CreateWallet"));
            Assert.Equal("", s.Driver.FindElement(By.Id("LNbankWallet")).GetAttribute("value"));
            
            // Create new wallet, which is pre-selected afterwards
            s.Driver.FindElement(By.Id("LNbank-CreateWallet")).Click();
            var walletName = "Wallet" + RandomUtils.GetUInt64();
            s.Driver.FindElement(By.Id("Wallet_Name")).SendKeys(walletName);
            s.Driver.FindElement(By.Id("LNbank-Create")).Click();
            s.Driver.WaitForElement(By.Id("LNbankWallet"));
            var walletSelect = new SelectElement(s.Driver.FindElement(By.Id("LNbankWallet")));
            Assert.Equal(walletName, walletSelect.SelectedOption.Text);
            
            // Finish and validate setup
            s.Driver.FindElement(By.Id("save")).Click();
            Assert.Contains("LNbank", s.Driver.FindElement(By.Id("CustomNodeInfo")).Text);
            
            // LNbank wallets
            s.Driver.FindElement(By.Id("Nav-LNbank")).Click();
            Assert.Contains(walletName, s.Driver.FindElement(By.Id("LNbank-Wallets")).Text);
            Assert.Single(s.Driver.FindElements(By.CssSelector("#LNbank-Wallets a")));
            s.Driver.FindElement(By.CssSelector("#LNbank-Wallets a")).Click();
            
            // Wallet
            Assert.Contains("0 sats", s.Driver.FindElement(By.Id("LNbank-WalletBalance")).Text);
            Assert.Contains("There are no transactions, yet.", s.Driver.FindElement(By.Id("LNbank-WalletTransactions")).Text);
            Assert.Single(s.Driver.FindElements(By.CssSelector("#LNbank-Wallets a")));
            s.Driver.FindElement(By.CssSelector("#LNbank-Wallets a")).Click();
            s.Driver.FindElement(By.Id("LNbank-WalletDetails")).Click();
            Assert.Contains(walletName, s.Driver.FindElement(By.Id("LNbank-WalletName")).Text);
            var walletId = s.Driver.FindElement(By.Id("LNbank-WalletId")).Text;
            s.Driver.FindElement(By.Id("LNbank-Back")).Click();
            
            // Receive
            var description = "First invoice";
            s.Driver.FindElement(By.Id("LNbank-WalletReceive")).Click();
            s.Driver.FindElement(By.Id("Description")).SendKeys(description);
            s.Driver.FindElement(By.Id("Amount")).Clear();
            s.Driver.FindElement(By.Id("Amount")).SendKeys("21");
            s.Driver.FindElement(By.Id("LNbank-CreateInvoice")).Click();
            
            // Details
            Assert.Contains(description, s.Driver.FindElement(By.Id("LNbank-TransactionDescription")).Text);
            Assert.Contains("21 sats unpaid", s.Driver.FindElement(By.Id("LNbank-TransactionAmount")).Text);
            var bolt11 = s.Driver.FindElement(By.Id("LNbank-CopyPaymentRequest")).GetAttribute("data-clipboard");
            Assert.StartsWith("ln", bolt11);
            s.Driver.FindElement(By.Id("LNbank-Back")).Click();
            
            // List
            Assert.Single(s.Driver.FindElements(By.CssSelector("#LNbank-WalletTransactions tr")));
            Assert.Contains("21 sats", s.Driver.FindElement(By.CssSelector("#LNbank-WalletTransactions tr .transaction-amount")).Text);
            Assert.Contains("unpaid", s.Driver.FindElement(By.CssSelector("#LNbank-WalletTransactions tr .transaction-status")).Text);
            Assert.Contains("0 sats", s.Driver.FindElement(By.Id("LNbank-WalletBalance")).Text);
            
            // Pay invoice
            /*
            var resp = await s.Server.CustomerLightningD.Pay(bolt11);
            Assert.Equal(PayResult.Ok, resp.Result);
            Thread.Sleep(5000);
            s.Driver.Navigate().Refresh();
            Thread.Sleep(5000);
            s.Driver.Navigate().Refresh();
            Assert.Single(s.Driver.FindElements(By.CssSelector("#LNbank-WalletTransactions tr")));
            Assert.Contains("21 sats", s.Driver.FindElement(By.CssSelector("#LNbank-WalletTransactions tr .transaction-settled")).Text);
            Assert.Contains("paid", s.Driver.FindElement(By.CssSelector("#LNbank-WalletTransactions tr .transaction-status")).Text);
            Assert.Contains("21 sats", s.Driver.FindElement(By.Id("LNbank-WalletBalance")).Text);
            
            // Send
            var amount = LightMoney.Satoshis(5000);
            var invoice = await s.Server.MerchantLnd.Client.CreateInvoice(amount, "Donation", TimeSpan.FromHours(1));
            
            s.Driver.FindElement(By.Id("LNbank-WalletSend")).Click();
            s.Driver.FindElement(By.Id("PaymentRequest")).SendKeys(invoice.BOLT11);
            s.Driver.FindElement(By.Id("LNbank-Decode")).Click();
            
            // Confirm
            Assert.Contains("Donation", s.Driver.FindElement(By.Id("LNbank-Description")).Text);
            Assert.Contains("5 sats", s.Driver.FindElement(By.Id("LNbank-Amount")).Text);
            s.Driver.FindElement(By.Id("LNbank-Send")).Click();
            
            // List
            Assert.Equal(2, s.Driver.FindElements(By.CssSelector("#LNbank-WalletTransactions tr")).Count);
            Assert.Contains("21 sats", s.Driver.FindElement(By.CssSelector("#LNbank-WalletTransactions tr .transaction-amount")).Text);
            Assert.Contains("unpaid", s.Driver.FindElement(By.CssSelector("#LNbank-WalletTransactions tr .transaction-status")).Text);
            Assert.Contains("16 sats", s.Driver.FindElement(By.Id("LNbank-WalletBalance")).Text);*/
        }
    }
}
