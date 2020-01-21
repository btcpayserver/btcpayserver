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
using System.Text.RegularExpressions;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    public class ChromeTests
    {
        public const int TestTimeout = TestUtils.TestTimeout;
        public ChromeTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanNavigateServerSettings()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser(true);
                s.Driver.FindElement(By.Id("ServerSettings")).Click();
                s.Driver.AssertNoError();
                s.ClickOnAllSideMenus();
                s.Driver.FindElement(By.LinkText("Services")).Click();

                Logs.Tester.LogInformation("Let's check if we can access the logs");
                s.Driver.FindElement(By.LinkText("Logs")).Click();
                s.Driver.FindElement(By.PartialLinkText(".log")).Click();
                Assert.Contains("Starting listening NBXplorer", s.Driver.PageSource);
                s.Driver.Quit();
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLndSeedBackup()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Server.ActivateLightning();
                await s.StartAsync();
                s.RegisterNewUser(true);
                s.Driver.FindElement(By.Id("ServerSettings")).Click();
                s.Driver.AssertNoError();
                s.Driver.FindElement(By.LinkText("Services")).Click();

                Logs.Tester.LogInformation("Let's if we can access LND's seed");
                Assert.Contains("server/services/lndseedbackup/BTC", s.Driver.PageSource);
                s.Driver.Navigate().GoToUrl(s.Link("/server/services/lndseedbackup/BTC"));
                s.Driver.FindElement(By.Id("details")).Click();
                var seedEl = s.Driver.FindElement(By.Id("SeedTextArea"));
                Assert.True(seedEl.Displayed);
                Assert.Contains("about over million", seedEl.Text, StringComparison.OrdinalIgnoreCase);
                var passEl = s.Driver.FindElement(By.Id("PasswordInput"));
                Assert.True(passEl.Displayed);
                Assert.Contains(passEl.Text, "hellorockstar", StringComparison.OrdinalIgnoreCase);
                s.Driver.FindElement(By.Id("delete")).Click();
                s.Driver.FindElement(By.Id("continue")).Click();
                s.AssertHappyMessage();
                seedEl = s.Driver.FindElement(By.Id("SeedTextArea"));
                Assert.Contains("Seed removed", seedEl.Text, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task NewUserLogin()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                //Register & Log Out
                var email = s.RegisterNewUser();
                s.Driver.FindElement(By.Id("Logout")).Click();
                s.Driver.AssertNoError();
                Assert.Contains("Account/Login", s.Driver.Url);
                // Should show the Tor address
                Assert.Contains("wsaxew3qa5ljfuenfebmaf3m5ykgatct3p6zjrqwoouj3foererde3id.onion", s.Driver.PageSource);

                s.Driver.Navigate().GoToUrl(s.Link("/invoices"));
                Assert.Contains("ReturnUrl=%2Finvoices", s.Driver.Url);

                // We should be redirected to login
                //Same User Can Log Back In
                s.Driver.FindElement(By.Id("Email")).SendKeys(email);
                s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
                s.Driver.FindElement(By.Id("LoginButton")).Click();

                // We should be redirected to invoice
                Assert.EndsWith("/invoices", s.Driver.Url);

                // Should not be able to reach server settings
                s.Driver.Navigate().GoToUrl(s.Link("/server/users"));
                Assert.Contains("ReturnUrl=%2Fserver%2Fusers", s.Driver.Url);

                //Change Password & Log Out
                s.Driver.FindElement(By.Id("MySettings")).Click();
                s.Driver.FindElement(By.Id("ChangePassword")).Click();
                s.Driver.FindElement(By.Id("OldPassword")).SendKeys("123456");
                s.Driver.FindElement(By.Id("NewPassword")).SendKeys("abc???");
                s.Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("abc???");
                s.Driver.FindElement(By.Id("UpdatePassword")).Click();
                s.Driver.FindElement(By.Id("Logout")).Click();
                s.Driver.AssertNoError();

                //Log In With New Password
                s.Driver.FindElement(By.Id("Email")).SendKeys(email);
                s.Driver.FindElement(By.Id("Password")).SendKeys("abc???");
                s.Driver.FindElement(By.Id("LoginButton")).Click();
                Assert.True(s.Driver.PageSource.Contains("Stores"), "Can't Access Stores");

                s.Driver.FindElement(By.Id("MySettings")).Click();
                s.ClickOnAllSideMenus();

                s.Driver.Quit();
            }
        }

        static void LogIn(SeleniumTester s, string email)
        {
            s.Driver.FindElement(By.Id("Email")).SendKeys(email);
            s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
            s.Driver.FindElement(By.Id("LoginButton")).Click();
            s.Driver.AssertNoError();
        }
        [Fact(Timeout = TestTimeout)]
        public async Task CanUseSSHService()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                var alice = s.RegisterNewUser(isAdmin: true);
                s.Driver.Navigate().GoToUrl(s.Link("/server/services"));
                Assert.Contains("server/services/ssh", s.Driver.PageSource);
                using (var client = await s.Server.PayTester.GetService<BTCPayServer.Configuration.BTCPayServerOptions>().SSHSettings.ConnectAsync())
                {
                    var result = await client.RunBash("echo hello");
                    Assert.Equal(string.Empty, result.Error);
                    Assert.Equal("hello\n", result.Output);
                    Assert.Equal(0, result.ExitStatus);
                }
                s.Driver.Navigate().GoToUrl(s.Link("/server/services/ssh"));
                s.Driver.AssertNoError();
                s.Driver.FindElement(By.Id("SSHKeyFileContent")).Clear();
                s.Driver.FindElement(By.Id("SSHKeyFileContent")).SendKeys("tes't\r\ntest2");
                s.Driver.FindElement(By.Id("submit")).ForceClick();
                s.Driver.AssertNoError();

                var text = s.Driver.FindElement(By.Id("SSHKeyFileContent")).Text;
                // Browser replace \n to \r\n, so it is hard to compare exactly what we want
                Assert.Contains("tes't", text);
                Assert.Contains("test2", text);
                Assert.True(s.Driver.PageSource.Contains("authorized_keys has been updated", StringComparison.OrdinalIgnoreCase));

                s.Driver.FindElement(By.Id("SSHKeyFileContent")).Clear();
                s.Driver.FindElement(By.Id("submit")).ForceClick();

                text = s.Driver.FindElement(By.Id("SSHKeyFileContent")).Text;
                Assert.DoesNotContain("test2", text);
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseDynamicDns()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                var alice = s.RegisterNewUser(isAdmin: true);
                s.Driver.Navigate().GoToUrl(s.Link("/server/services"));
                Assert.Contains("Dynamic DNS", s.Driver.PageSource);

                s.Driver.Navigate().GoToUrl(s.Link("/server/services/dynamic-dns"));
                s.Driver.AssertNoError();
                if (s.Driver.PageSource.Contains("pouet.hello.com"))
                {
                    // Cleanup old test run
                    s.Driver.Navigate().GoToUrl(s.Link("/server/services/dynamic-dns/pouet.hello.com/delete"));
                    s.Driver.FindElement(By.Id("continue")).Click();
                }
                s.Driver.FindElement(By.Id("AddDynamicDNS")).Click();
                s.Driver.AssertNoError();
                // We will just cheat for test purposes by only querying the server
                s.Driver.FindElement(By.Id("ServiceUrl")).SendKeys(s.Link("/"));
                s.Driver.FindElement(By.Id("Settings_Hostname")).SendKeys("pouet.hello.com");
                s.Driver.FindElement(By.Id("Settings_Login")).SendKeys("MyLog");
                s.Driver.FindElement(By.Id("Settings_Password")).SendKeys("MyLog" + Keys.Enter);
                s.Driver.AssertNoError();
                Assert.Contains("The Dynamic DNS has been successfully queried", s.Driver.PageSource);
                Assert.EndsWith("/server/services/dynamic-dns", s.Driver.Url);

                // Try to do the same thing should fail (hostname already exists)
                s.Driver.FindElement(By.Id("AddDynamicDNS")).Click();
                s.Driver.AssertNoError();
                s.Driver.FindElement(By.Id("ServiceUrl")).SendKeys(s.Link("/"));
                s.Driver.FindElement(By.Id("Settings_Hostname")).SendKeys("pouet.hello.com");
                s.Driver.FindElement(By.Id("Settings_Login")).SendKeys("MyLog");
                s.Driver.FindElement(By.Id("Settings_Password")).SendKeys("MyLog" + Keys.Enter);
                s.Driver.AssertNoError();
                Assert.Contains("This hostname already exists", s.Driver.PageSource);

                // Delete it
                s.Driver.Navigate().GoToUrl(s.Link("/server/services/dynamic-dns"));
                Assert.Contains("/server/services/dynamic-dns/pouet.hello.com/delete", s.Driver.PageSource);
                s.Driver.Navigate().GoToUrl(s.Link("/server/services/dynamic-dns/pouet.hello.com/delete"));
                s.Driver.FindElement(By.Id("continue")).Click();
                s.Driver.AssertNoError();

                Assert.DoesNotContain("/server/services/dynamic-dns/pouet.hello.com/delete", s.Driver.PageSource);
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanCreateStores()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                var alice = s.RegisterNewUser();
                var store = s.CreateNewStore().storeName;
                s.AddDerivationScheme();
                s.Driver.AssertNoError();
                Assert.Contains(store, s.Driver.PageSource);
                var storeUrl = s.Driver.Url;
                s.ClickOnAllSideMenus();
                s.GoToInvoices();
                s.CreateInvoice(store);
                s.AssertHappyMessage();
                s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
                var invoiceUrl = s.Driver.Url;

                // When logout we should not be able to access store and invoice details
                s.Driver.FindElement(By.Id("Logout")).Click();
                s.Driver.Navigate().GoToUrl(storeUrl);
                Assert.Contains("ReturnUrl", s.Driver.Url);
                s.Driver.Navigate().GoToUrl(invoiceUrl);
                Assert.Contains("ReturnUrl", s.Driver.Url);
                s.GoToRegister();
                // When logged we should not be able to access store and invoice details
                var bob = s.RegisterNewUser();
                s.Driver.Navigate().GoToUrl(storeUrl);
                Assert.Contains("ReturnUrl", s.Driver.Url);
                s.Driver.Navigate().GoToUrl(invoiceUrl);
                s.AssertNotFound();
                s.GoToHome();
                s.Logout();

                // Let's add Bob as a guest to alice's store
                LogIn(s, alice);
                s.Driver.Navigate().GoToUrl(storeUrl + "/users");
                s.Driver.FindElement(By.Id("Email")).SendKeys(bob + Keys.Enter);
                Assert.Contains("User added successfully", s.Driver.PageSource);
                s.Logout();

                // Bob should not have access to store, but should have access to invoice
                LogIn(s, bob);
                s.Driver.Navigate().GoToUrl(storeUrl);
                Assert.Contains("ReturnUrl", s.Driver.Url);
                s.Driver.Navigate().GoToUrl(invoiceUrl);
                s.Driver.AssertNoError();

                // Alice should be able to delete the store
                s.Logout();
                LogIn(s, alice);
                s.Driver.FindElement(By.Id("Stores")).Click();
                s.Driver.FindElement(By.LinkText("Remove")).Click();
                s.Driver.FindElement(By.Id("continue")).Click();
                s.Driver.FindElement(By.Id("Stores")).Click();
                s.Driver.Navigate().GoToUrl(storeUrl);
                Assert.Contains("ReturnUrl", s.Driver.Url);
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUsePairing()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.Driver.Navigate().GoToUrl(s.Link("/api-access-request"));
                Assert.Contains("ReturnUrl", s.Driver.Url);
                s.GoToRegister();
                var alice = s.RegisterNewUser();
                var store = s.CreateNewStore().storeName;
                s.AddDerivationScheme();

                s.Driver.FindElement(By.Id("Tokens")).Click();
                s.Driver.FindElement(By.Id("CreateNewToken")).Click();
                s.Driver.FindElement(By.Id("RequestPairing")).Click();
                string pairingCode = AssertUrlHasPairingCode(s);

                s.Driver.FindElement(By.Id("ApprovePairing")).Click();
                s.AssertHappyMessage();
                Assert.Contains(pairingCode, s.Driver.PageSource);

                var client = new NBitpayClient.Bitpay(new Key(), s.Server.PayTester.ServerUri);
                await client.AuthorizeClient(new NBitpayClient.PairingCode(pairingCode));
                await client.CreateInvoiceAsync(new NBitpayClient.Invoice()
                {
                    Price = 0.000000012m,
                    Currency = "USD",
                    FullNotifications = true
                }, NBitpayClient.Facade.Merchant);

                client = new NBitpayClient.Bitpay(new Key(), s.Server.PayTester.ServerUri);

                var code = await client.RequestClientAuthorizationAsync("hehe", NBitpayClient.Facade.Merchant);
                s.Driver.Navigate().GoToUrl(code.CreateLink(s.Server.PayTester.ServerUri));
                s.Driver.FindElement(By.Id("ApprovePairing")).Click();

                await client.CreateInvoiceAsync(new NBitpayClient.Invoice()
                {
                    Price = 0.000000012m,
                    Currency = "USD",
                    FullNotifications = true
                }, NBitpayClient.Facade.Merchant);

                s.Driver.Navigate().GoToUrl(s.Link("/api-tokens"));
                s.Driver.FindElement(By.Id("RequestPairing")).Click();
                s.Driver.FindElement(By.Id("ApprovePairing")).Click();
                AssertUrlHasPairingCode(s);
            }
        }

        private static string AssertUrlHasPairingCode(SeleniumTester s)
        {
            var regex = Regex.Match(new Uri(s.Driver.Url, UriKind.Absolute).Query, "pairingCode=([^&]*)");
            Assert.True(regex.Success, $"{s.Driver.Url} does not match expected regex");
            var pairingCode = regex.Groups[1].Value;
            return pairingCode;
        }


        [Fact(Timeout = TestTimeout)]
        public async Task CanCreateAppPoS()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser();
                var store = s.CreateNewStore();

                s.Driver.FindElement(By.Id("Apps")).Click();
                s.Driver.FindElement(By.Id("CreateNewApp")).Click();
                s.Driver.FindElement(By.Name("Name")).SendKeys("PoS" + Guid.NewGuid());
                s.Driver.FindElement(By.Id("SelectedAppType")).SendKeys("PointOfSale" + Keys.Enter);
                s.Driver.FindElement(By.Id("SelectedStore")).SendKeys(store + Keys.Enter);
                s.Driver.FindElement(By.Id("Create")).Click();
                s.Driver.FindElement(By.Id("EnableShoppingCart")).Click();
                s.Driver.FindElement(By.Id("SaveSettings")).ForceClick();
                s.Driver.FindElement(By.Id("ViewApp")).ForceClick();
                s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
                Assert.True(s.Driver.PageSource.Contains("Tea shop"), "Unable to create PoS");
                s.Driver.Quit();
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanCreateAppCF()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme();

                s.Driver.FindElement(By.Id("Apps")).Click();
                s.Driver.FindElement(By.Id("CreateNewApp")).Click();
                s.Driver.FindElement(By.Name("Name")).SendKeys("CF" + Guid.NewGuid());
                s.Driver.FindElement(By.Id("SelectedAppType")).SendKeys("Crowdfund" + Keys.Enter);
                s.Driver.FindElement(By.Id("SelectedStore")).SendKeys(store + Keys.Enter);
                s.Driver.FindElement(By.Id("Create")).Click();
                s.Driver.FindElement(By.Id("Title")).SendKeys("Kukkstarter");
                s.Driver.FindElement(By.CssSelector("div.note-editable.card-block")).SendKeys("1BTC = 1BTC");
                s.Driver.FindElement(By.Id("TargetCurrency")).SendKeys("JPY");
                s.Driver.FindElement(By.Id("TargetAmount")).SendKeys("700");
                s.Driver.FindElement(By.Id("SaveSettings")).ForceClick();
                s.Driver.FindElement(By.Id("ViewApp")).ForceClick();
                s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
                Assert.True(s.Driver.PageSource.Contains("Currently Active!"), "Unable to create CF");
                s.Driver.Quit();
            } 
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanCreatePayRequest()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser();
                s.CreateNewStore();
                s.AddDerivationScheme();

                s.Driver.FindElement(By.Id("PaymentRequests")).Click();
                s.Driver.FindElement(By.Id("CreatePaymentRequest")).Click();
                s.Driver.FindElement(By.Id("Title")).SendKeys("Pay123");
                s.Driver.FindElement(By.Id("Amount")).SendKeys("700");
                s.Driver.FindElement(By.Id("Currency")).SendKeys("BTC");
                s.Driver.FindElement(By.Id("SaveButton")).ForceClick();
                s.Driver.FindElement(By.Name("ViewAppButton")).SendKeys(Keys.Return);
                s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
                Assert.True(s.Driver.PageSource.Contains("Amount due"), "Unable to create Payment Request");
                s.Driver.Quit();
            }
        }
        
        [Fact(Timeout = TestTimeout)]
        public async Task CanManageWallet()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser(true);
               var storeId =  s.CreateNewStore();

                // In this test, we try to spend from a manual seed. We import the xpub 49'/0'/0', then try to use the seed 
                // to sign the transaction
                s.GenerateWallet("BTC", "", true, false);
               
                //let's test quickly the receive wallet page
                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();
                
                s.Driver.FindElement(By.Id("WalletSend")).Click();
                s.Driver.ScrollTo(By.Id("SendMenu"));
                s.Driver.FindElement(By.Id("SendMenu")).ForceClick();
                //you cant use the Sign with NBX option without saving private keys when generating the wallet.
                Assert.DoesNotContain("nbx-seed", s.Driver.PageSource);
                
                s.Driver.FindElement(By.Id("WalletReceive")).Click();
                //generate a receiving address
                s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
                Assert.True(s.Driver.FindElement(By.ClassName("qr-container")).Displayed);
                var receiveAddr = s.Driver.FindElement(By.Id("vue-address")).GetAttribute("value");
                //unreserve
                s.Driver.FindElement(By.CssSelector("button[value=unreserve-current-address]")).Click();
                //generate it again, should be the same one as before as nothign got used in the meantime
                s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
                Assert.True(s.Driver.FindElement(By.ClassName("qr-container")).Displayed);
                Assert.Equal( receiveAddr, s.Driver.FindElement(By.Id("vue-address")).GetAttribute("value"));
                
                //send money to addr and ensure it changed
                
                var sess = await s.Server.ExplorerClient.CreateWebsocketNotificationSessionAsync();
                sess.ListenAllTrackedSource();
                var nextEvent = sess.NextEventAsync();
                s.Server.ExplorerNode.SendToAddress(BitcoinAddress.Create(receiveAddr, Network.RegTest),
                    Money.Parse("0.1"));
                await nextEvent;
                await Task.Delay(200);
                s.Driver.Navigate().Refresh();
                s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
                Assert.NotEqual( receiveAddr, s.Driver.FindElement(By.Id("vue-address")).GetAttribute("value"));
                receiveAddr = s.Driver.FindElement(By.Id("vue-address")).GetAttribute("value");
                //change the wallet and ensure old address is not there and generating a new one does not result in the prev one
                s.GoToStore(storeId.storeId);
                s.GenerateWallet("BTC", "", true, false);
                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();
                s.Driver.FindElement(By.Id("WalletReceive")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
                Assert.NotEqual( receiveAddr, s.Driver.FindElement(By.Id("vue-address")).GetAttribute("value"));
                
                
                var invoiceId = s.CreateInvoice(storeId.storeId);
                var invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
                var address = invoice.EntityToDTO().Addresses["BTC"];
                

                //wallet should have been imported to bitcoin core wallet in watch only mode.
                var result = await s.Server.ExplorerNode.GetAddressInfoAsync(BitcoinAddress.Create(address, Network.RegTest));
                Assert.True(result.IsWatchOnly);
                s.GoToStore(storeId.storeId);
                var mnemonic = s.GenerateWallet("BTC", "", true, true);
                
                //lets import and save private keys
                var root = new Mnemonic(mnemonic).DeriveExtKey();
                 invoiceId = s.CreateInvoice(storeId.storeId);
                 invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice( invoiceId);
                 address = invoice.EntityToDTO().Addresses["BTC"];
                 result = await s.Server.ExplorerNode.GetAddressInfoAsync(BitcoinAddress.Create(address, Network.RegTest));
                 //spendable from bitcoin core wallet!
                 Assert.False(result.IsWatchOnly);
                var tx = s.Server.ExplorerNode.SendToAddress(BitcoinAddress.Create(address, Network.RegTest), Money.Coins(3.0m));
                s.Server.ExplorerNode.Generate(1);

                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();

                s.ClickOnAllSideMenus();

                // Make sure we can rescan, because we are admin!
                s.Driver.FindElement(By.Id("WalletRescan")).ForceClick();
                Assert.Contains("The batch size make sure", s.Driver.PageSource);

                // We setup the fingerprint and the account key path
                s.Driver.FindElement(By.Id("WalletSettings")).ForceClick();
//                s.Driver.FindElement(By.Id("AccountKeys_0__MasterFingerprint")).SendKeys("8bafd160");
//                s.Driver.FindElement(By.Id("AccountKeys_0__AccountKeyPath")).SendKeys("m/49'/0'/0'" + Keys.Enter);

                // Check the tx sent earlier arrived
                s.Driver.FindElement(By.Id("WalletTransactions")).ForceClick();
                var walletTransactionLink = s.Driver.Url;
                Assert.Contains(tx.ToString(), s.Driver.PageSource);

                
                void SignWith(string signingSource)
                {
                    // Send to bob
                    s.Driver.FindElement(By.Id("WalletSend")).Click();
                    var bob = new Key().PubKey.Hash.GetAddress(Network.RegTest);
                    SetTransactionOutput(0, bob, 1);
                    s.Driver.ScrollTo(By.Id("SendMenu"));
                    s.Driver.FindElement(By.Id("SendMenu")).ForceClick();
                    s.Driver.FindElement(By.CssSelector("button[value=seed]")).Click();

                    // Input the seed
                    s.Driver.FindElement(By.Id("SeedOrKey")).SendKeys(signingSource + Keys.Enter);

                    // Broadcast
                    Assert.Contains(bob.ToString(), s.Driver.PageSource);
                    Assert.Contains("1.00000000", s.Driver.PageSource);
                    s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).ForceClick();
                    Assert.Equal(walletTransactionLink, s.Driver.Url);
                }

                void SetTransactionOutput(int index, BitcoinAddress dest, decimal amount, bool subtract = false)
                {
                    s.Driver.FindElement(By.Id($"Outputs_{index}__DestinationAddress")).SendKeys(dest.ToString());
                    var amountElement = s.Driver.FindElement(By.Id($"Outputs_{index}__Amount"));
                    amountElement.Clear();
                    amountElement.SendKeys(amount.ToString());
                    var checkboxElement = s.Driver.FindElement(By.Id($"Outputs_{index}__SubtractFeesFromOutput"));
                    if (checkboxElement.Selected != subtract)
                    {
                        checkboxElement.Click();
                    }
                }
                
                SignWith(mnemonic);
                
                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();
                s.Driver.FindElement(By.Id("WalletSend")).Click();
                
                var jack = new Key().PubKey.Hash.GetAddress(Network.RegTest);
                SetTransactionOutput(0, jack, 0.01m);
                s.Driver.ScrollTo(By.Id("SendMenu"));
                s.Driver.FindElement(By.Id("SendMenu")).ForceClick();
                
                s.Driver.FindElement(By.CssSelector("button[value=nbx-seed]")).Click();
                Assert.Contains(jack.ToString(), s.Driver.PageSource);
                Assert.Contains("0.01000000", s.Driver.PageSource);
                s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).ForceClick();
                Assert.Equal(walletTransactionLink, s.Driver.Url);
                
                
            }
        }
    }
}
