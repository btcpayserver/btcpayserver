using System;
using Xunit;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using BTCPayServer.Tests.Logging;
using Xunit.Abstractions;
using OpenQA.Selenium.Interactions;
using System.Linq;
using NBitcoin;
using System.Threading.Tasks;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    [Collection("Selenium collection")]
    public class ChromeTests
    {
        public SeleniumTester SeleniumTester { get; }

        public ChromeTests(ITestOutputHelper helper, SeleniumTester seleniumTester)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
            SeleniumTester = seleniumTester;
        }

        [Fact]
        public void CanNavigateServerSettings()
        {
            SeleniumTester.RegisterNewUser(true);
            SeleniumTester.Driver.FindElement(By.Id("ServerSettings")).Click();
            SeleniumTester.Driver.AssertNoError();
            SeleniumTester.ClickOnAllSideMenus();
        }

        [Fact]
        public void NewUserLogin()
        {
            //Register & Log Out
            var email = SeleniumTester.RegisterNewUser();
            SeleniumTester.Driver.FindElement(By.Id("Logout")).Click();
            SeleniumTester.Driver.AssertNoError();
            SeleniumTester.Driver.FindElement(By.Id("Login")).Click();
            SeleniumTester.Driver.AssertNoError();

            SeleniumTester.Driver.Navigate().GoToUrl(SeleniumTester.Link("/invoices"));
            Assert.Contains("ReturnUrl=%2Finvoices", SeleniumTester.Driver.Url);

            // We should be redirected to login
            //Same User Can Log Back In
            SeleniumTester.Driver.FindElement(By.Id("Email")).SendKeys(email);
            SeleniumTester.Driver.FindElement(By.Id("Password")).SendKeys("123456");
            SeleniumTester.Driver.FindElement(By.Id("LoginButton")).Click();

            // We should be redirected to invoice
            Assert.EndsWith("/invoices", SeleniumTester.Driver.Url);

            // Should not be able to reach server settings
            SeleniumTester.Driver.Navigate().GoToUrl(SeleniumTester.Link("/server/users"));
            Assert.Contains("ReturnUrl=%2Fserver%2Fusers", SeleniumTester.Driver.Url);

            //Change Password & Log Out
            SeleniumTester.Driver.FindElement(By.Id("MySettings")).Click();
            SeleniumTester.Driver.FindElement(By.Id("ChangePassword")).Click();
            SeleniumTester.Driver.FindElement(By.Id("OldPassword")).SendKeys("123456");
            SeleniumTester.Driver.FindElement(By.Id("NewPassword")).SendKeys("abc???");
            SeleniumTester.Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("abc???");
            SeleniumTester.Driver.FindElement(By.Id("UpdatePassword")).Click();
            SeleniumTester.Driver.FindElement(By.Id("Logout")).Click();
            SeleniumTester.Driver.AssertNoError();

            //Log In With New Password
            SeleniumTester.Driver.FindElement(By.Id("Login")).Click();
            SeleniumTester.Driver.FindElement(By.Id("Email")).SendKeys(email);
            SeleniumTester.Driver.FindElement(By.Id("Password")).SendKeys("abc???");
            SeleniumTester.Driver.FindElement(By.Id("LoginButton")).Click();
            Assert.True(SeleniumTester.Driver.PageSource.Contains("Stores"), "Can't Access Stores");

            SeleniumTester.Driver.FindElement(By.Id("MySettings")).Click();
            SeleniumTester.ClickOnAllSideMenus();
        }

        static void LogIn(SeleniumTester s, string email)
        {
            s.Driver.FindElement(By.Id("Login")).Click();
            s.Driver.FindElement(By.Id("Email")).SendKeys(email);
            s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
            s.Driver.FindElement(By.Id("LoginButton")).Click();
            s.Driver.AssertNoError();
        }
        [Fact]
        public async Task CanUseSSHService()
        {
            var alice = SeleniumTester.RegisterNewUser(isAdmin: true);
            SeleniumTester.Driver.Navigate().GoToUrl(SeleniumTester.Link("/server/services"));
            Assert.Contains("server/services/ssh", SeleniumTester.Driver.PageSource);
            using (var client = await SeleniumTester.Server.PayTester.GetService<BTCPayServer.Configuration.BTCPayServerOptions>().SSHSettings.ConnectAsync())
            {
                var result = await client.RunBash("echo hello");
                Assert.Equal(string.Empty, result.Error);
                Assert.Equal("hello\n", result.Output);
                Assert.Equal(0, result.ExitStatus);
            }
            SeleniumTester.Driver.Navigate().GoToUrl(SeleniumTester.Link("/server/services/ssh"));
            SeleniumTester.Driver.AssertNoError();
        }

        [Fact]
        public void CanUseDynamicDns()
        {
            var alice = SeleniumTester.RegisterNewUser(isAdmin: true);
            SeleniumTester.Driver.Navigate().GoToUrl(SeleniumTester.Link("/server/services"));
            Assert.Contains("Dynamic DNS", SeleniumTester.Driver.PageSource);

            SeleniumTester.Driver.Navigate().GoToUrl(SeleniumTester.Link("/server/services/dynamic-dns"));
            SeleniumTester.Driver.AssertNoError();
            if (SeleniumTester.Driver.PageSource.Contains("pouet.hello.com"))
            {
                // Cleanup old test run
                SeleniumTester.Driver.Navigate().GoToUrl(SeleniumTester.Link("/server/services/dynamic-dns/pouet.hello.com/delete"));
                SeleniumTester.Driver.FindElement(By.Id("continue")).Click();
            }
            SeleniumTester.Driver.FindElement(By.Id("AddDynamicDNS")).Click();
            SeleniumTester.Driver.AssertNoError();
            // We will just cheat for test purposes by only querying the server
            SeleniumTester.Driver.FindElement(By.Id("ServiceUrl")).SendKeys(SeleniumTester.Link("/"));
            SeleniumTester.Driver.FindElement(By.Id("Settings_Hostname")).SendKeys("pouet.hello.com");
            SeleniumTester.Driver.FindElement(By.Id("Settings_Login")).SendKeys("MyLog");
            SeleniumTester.Driver.FindElement(By.Id("Settings_Password")).SendKeys("MyLog" + Keys.Enter);
            SeleniumTester.Driver.AssertNoError();
            Assert.Contains("The Dynamic DNS has been successfully queried", SeleniumTester.Driver.PageSource);
            Assert.EndsWith("/server/services/dynamic-dns", SeleniumTester.Driver.Url);

            // Try to do the same thing should fail (hostname already exists)
            SeleniumTester.Driver.FindElement(By.Id("AddDynamicDNS")).Click();
            SeleniumTester.Driver.AssertNoError();
            SeleniumTester.Driver.FindElement(By.Id("ServiceUrl")).SendKeys(SeleniumTester.Link("/"));
            SeleniumTester.Driver.FindElement(By.Id("Settings_Hostname")).SendKeys("pouet.hello.com");
            SeleniumTester.Driver.FindElement(By.Id("Settings_Login")).SendKeys("MyLog");
            SeleniumTester.Driver.FindElement(By.Id("Settings_Password")).SendKeys("MyLog" + Keys.Enter);
            SeleniumTester.Driver.AssertNoError();
            Assert.Contains("This hostname already exists", SeleniumTester.Driver.PageSource);

            // Delete it
            SeleniumTester.Driver.Navigate().GoToUrl(SeleniumTester.Link("/server/services/dynamic-dns"));
            Assert.Contains("/server/services/dynamic-dns/pouet.hello.com/delete", SeleniumTester.Driver.PageSource);
            SeleniumTester.Driver.Navigate().GoToUrl(SeleniumTester.Link("/server/services/dynamic-dns/pouet.hello.com/delete"));
            SeleniumTester.Driver.FindElement(By.Id("continue")).Click();
            SeleniumTester.Driver.AssertNoError();

            Assert.DoesNotContain("/server/services/dynamic-dns/pouet.hello.com/delete", SeleniumTester.Driver.PageSource);
        }

        [Fact]
        public void CanCreateStores()
        {
            var alice = SeleniumTester.RegisterNewUser();
            var store = SeleniumTester.CreateNewStore().storeName;
            SeleniumTester.AddDerivationScheme();
            SeleniumTester.Driver.AssertNoError();
            Assert.Contains(store, SeleniumTester.Driver.PageSource);
            var storeUrl = SeleniumTester.Driver.Url;
            SeleniumTester.ClickOnAllSideMenus();
            SeleniumTester.GoToInvoices();
            SeleniumTester.CreateInvoice(store);
            SeleniumTester.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
            var invoiceUrl = SeleniumTester.Driver.Url;

            // When logout we should not be able to access store and invoice details
            SeleniumTester.Driver.FindElement(By.Id("Logout")).Click();
            SeleniumTester.Driver.Navigate().GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", SeleniumTester.Driver.Url);
            SeleniumTester.Driver.Navigate().GoToUrl(invoiceUrl);
            Assert.Contains("ReturnUrl", SeleniumTester.Driver.Url);

            // When logged we should not be able to access store and invoice details
            var bob = SeleniumTester.RegisterNewUser();
            SeleniumTester.Driver.Navigate().GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", SeleniumTester.Driver.Url);
            SeleniumTester.Driver.Navigate().GoToUrl(invoiceUrl);
            SeleniumTester.AssertNotFound();
            SeleniumTester.GoToHome();
            SeleniumTester.Logout();

            // Let's add Bob as a guest to alice's store
            LogIn(SeleniumTester, alice);
            SeleniumTester.Driver.Navigate().GoToUrl(storeUrl + "/users");
            SeleniumTester.Driver.FindElement(By.Id("Email")).SendKeys(bob + Keys.Enter);
            Assert.Contains("User added successfully", SeleniumTester.Driver.PageSource);
            SeleniumTester.Logout();

            // Bob should not have access to store, but should have access to invoice
            LogIn(SeleniumTester, bob);
            SeleniumTester.Driver.Navigate().GoToUrl(storeUrl);
            Assert.Contains("ReturnUrl", SeleniumTester.Driver.Url);
            SeleniumTester.Driver.Navigate().GoToUrl(invoiceUrl);
            SeleniumTester.Driver.AssertNoError();
        }



        [Fact]
        public void CanCreateAppPoS()
        {
            SeleniumTester.RegisterNewUser();
            var store = SeleniumTester.CreateNewStore();

            SeleniumTester.Driver.FindElement(By.Id("Apps")).Click();
            SeleniumTester.Driver.FindElement(By.Id("CreateNewApp")).Click();
            SeleniumTester.Driver.FindElement(By.Name("Name")).SendKeys("PoS" + Guid.NewGuid());
            SeleniumTester.Driver.FindElement(By.Id("SelectedAppType")).SendKeys("PointOfSale" + Keys.Enter);
            SeleniumTester.Driver.FindElement(By.Id("SelectedStore")).SendKeys(store + Keys.Enter);
            SeleniumTester.Driver.FindElement(By.Id("Create")).Click();
            SeleniumTester.Driver.FindElement(By.Id("EnableShoppingCart")).Click();
            SeleniumTester.Driver.FindElement(By.Id("SaveSettings")).ForceClick();
            SeleniumTester.Driver.FindElement(By.Id("ViewApp")).ForceClick();
            SeleniumTester.Driver.SwitchTo().Window(SeleniumTester.Driver.WindowHandles.Last());
            Assert.True(SeleniumTester.Driver.PageSource.Contains("Tea shop"), "Unable to create PoS");
        }

        [Fact]
        public void CanCreateAppCF()
        {
            SeleniumTester.RegisterNewUser();
            var store = SeleniumTester.CreateNewStore();
            SeleniumTester.AddDerivationScheme();

            SeleniumTester.Driver.FindElement(By.Id("Apps")).Click();
            SeleniumTester.Driver.FindElement(By.Id("CreateNewApp")).Click();
            SeleniumTester.Driver.FindElement(By.Name("Name")).SendKeys("CF" + Guid.NewGuid());
            SeleniumTester.Driver.FindElement(By.Id("SelectedAppType")).SendKeys("Crowdfund" + Keys.Enter);
            SeleniumTester.Driver.FindElement(By.Id("SelectedStore")).SendKeys(store + Keys.Enter);
            SeleniumTester.Driver.FindElement(By.Id("Create")).Click();
            SeleniumTester.Driver.FindElement(By.Id("Title")).SendKeys("Kukkstarter");
            SeleniumTester.Driver.FindElement(By.CssSelector("div.note-editable.card-block")).SendKeys("1BTC = 1BTC");
            SeleniumTester.Driver.FindElement(By.Id("TargetCurrency")).SendKeys("JPY");
            SeleniumTester.Driver.FindElement(By.Id("TargetAmount")).SendKeys("700");
            SeleniumTester.Driver.FindElement(By.Id("SaveSettings")).ForceClick();
            SeleniumTester.Driver.FindElement(By.Id("ViewApp")).ForceClick();
            SeleniumTester.Driver.SwitchTo().Window(SeleniumTester.Driver.WindowHandles.Last());
            Assert.True(SeleniumTester.Driver.PageSource.Contains("Currently Active!"), "Unable to create CF");
        }

        [Fact]
        public void CanCreatePayRequest()
        {
            SeleniumTester.RegisterNewUser();
            SeleniumTester.CreateNewStore();
            SeleniumTester.AddDerivationScheme();

            SeleniumTester.Driver.FindElement(By.Id("PaymentRequests")).Click();
            SeleniumTester.Driver.FindElement(By.Id("CreatePaymentRequest")).Click();
            SeleniumTester.Driver.FindElement(By.Id("Title")).SendKeys("Pay123");
            SeleniumTester.Driver.FindElement(By.Id("Amount")).SendKeys("700");
            SeleniumTester.Driver.FindElement(By.Id("Currency")).SendKeys("BTC");
            SeleniumTester.Driver.FindElement(By.Id("SaveButton")).ForceClick();
            SeleniumTester.Driver.FindElement(By.Name("ViewAppButton")).SendKeys(Keys.Return);
            SeleniumTester.Driver.SwitchTo().Window(SeleniumTester.Driver.WindowHandles.Last());
            Assert.True(SeleniumTester.Driver.PageSource.Contains("Amount due"), "Unable to create Payment Request");
        }

        [Fact]
        public void CanManageWallet()
        {
            SeleniumTester.RegisterNewUser();
            SeleniumTester.CreateNewStore();

            // In this test, we try to spend from a manual seed. We import the xpub 49'/0'/0', then try to use the seed 
            // to sign the transaction
            var mnemonic = "usage fever hen zero slide mammal silent heavy donate budget pulse say brain thank sausage brand craft about save attract muffin advance illegal cabbage";
            var root = new Mnemonic(mnemonic).DeriveExtKey();
            SeleniumTester.AddDerivationScheme("BTC", "ypub6WWc2gWwHbdnAAyJDnR4SPL1phRh7REqrPBfZeizaQ1EmTshieRXJC3Z5YoU4wkcdKHEjQGkh6AYEzCQC1Kz3DNaWSwdc1pc8416hAjzqyD");
            var tx = SeleniumTester.Server.ExplorerNode.SendToAddress(BitcoinAddress.Create("bcrt1qmxg8fgnmkp354vhe78j6sr4ut64tyz2xyejel4", Network.RegTest), Money.Coins(3.0m));
            SeleniumTester.Server.ExplorerNode.Generate(1);

            SeleniumTester.Driver.FindElement(By.Id("Wallets")).Click();
            SeleniumTester.Driver.FindElement(By.LinkText("Manage")).Click();

            SeleniumTester.ClickOnAllSideMenus();

            // We setup the fingerprint and the account key path
            SeleniumTester.Driver.FindElement(By.Id("WalletSettings")).ForceClick();
            SeleniumTester.Driver.FindElement(By.Id("AccountKeys_0__MasterFingerprint")).SendKeys("8bafd160");
            SeleniumTester.Driver.FindElement(By.Id("AccountKeys_0__AccountKeyPath")).SendKeys("m/49'/0'/0'" + Keys.Enter);

            // Check the tx sent earlier arrived
            SeleniumTester.Driver.FindElement(By.Id("WalletTransactions")).ForceClick();
            var walletTransactionLink = SeleniumTester.Driver.Url;
            Assert.Contains(tx.ToString(), SeleniumTester.Driver.PageSource);


            void SignWith(string signingSource)
            {
                // Send to bob
                SeleniumTester.Driver.FindElement(By.Id("WalletSend")).Click();
                var bob = new Key().PubKey.Hash.GetAddress(Network.RegTest);
                SetTransactionOutput(0, bob, 1);
                SeleniumTester.Driver.ScrollTo(By.Id("SendMenu"));
                SeleniumTester.Driver.FindElement(By.Id("SendMenu")).ForceClick();
                SeleniumTester.Driver.FindElement(By.CssSelector("button[value=seed]")).Click();

                // Input the seed
                SeleniumTester.Driver.FindElement(By.Id("SeedOrKey")).SendKeys(signingSource + Keys.Enter);

                // Broadcast
                Assert.Contains(bob.ToString(), SeleniumTester.Driver.PageSource);
                Assert.Contains("1.00000000", SeleniumTester.Driver.PageSource);
                SeleniumTester.Driver.FindElement(By.CssSelector("button[value=broadcast]")).ForceClick();
                Assert.Equal(walletTransactionLink, SeleniumTester.Driver.Url);
            }

            void SetTransactionOutput(int index, BitcoinAddress dest, decimal amount, bool subtract = false)
            {
                SeleniumTester.Driver.FindElement(By.Id($"Outputs_{index}__DestinationAddress")).SendKeys(dest.ToString());
                var amountElement = SeleniumTester.Driver.FindElement(By.Id($"Outputs_{index}__Amount"));
                amountElement.Clear();
                amountElement.SendKeys(amount.ToString());
                var checkboxElement = SeleniumTester.Driver.FindElement(By.Id($"Outputs_{index}__SubtractFeesFromOutput"));
                if (checkboxElement.Selected != subtract)
                {
                    checkboxElement.Click();
                }
            }
            SignWith(mnemonic);
            var accountKey = root.Derive(new KeyPath("m/49'/0'/0'")).GetWif(Network.RegTest).ToString();
            SignWith(accountKey);
        }
    }
}
