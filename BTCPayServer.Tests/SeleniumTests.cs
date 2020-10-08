using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Wallets;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.Payment;
using NBitpayClient;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

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
                s.Logout();
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

                //let's test invite link
                s.Logout();
                s.GoToRegister();
                var newAdminUser =  s.RegisterNewUser(true);
                s.GoToServer(ServerNavPages.Users);
                s.Driver.FindElement(By.Id("CreateUser")).Click();
                
                var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
                s.Driver.FindElement(By.Id("Email")).SendKeys(usr);
                s.Driver.FindElement(By.Id("Save")).Click();
                var url = s.AssertHappyMessage().FindElement(By.TagName("a")).Text;;
                s.Logout();
                s.Driver.Navigate().GoToUrl(url);
                Assert.Equal("hidden",s.Driver.FindElement(By.Id("Email")).GetAttribute("type"));
                Assert.Equal(usr,s.Driver.FindElement(By.Id("Email")).GetAttribute("value"));
                
                s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
                s.Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("123456");
                s.Driver.FindElement(By.Id("SetPassword")).Click();
                s.AssertHappyMessage();
                s.Driver.FindElement(By.Id("Email")).SendKeys(usr);
                s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
                s.Driver.FindElement(By.Id("LoginButton")).Click();

                // We should be logged in now
                s.Driver.FindElement(By.Id("mainNav"));
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
        public async Task CanSetupEmailServer()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                var alice = s.RegisterNewUser(isAdmin: true);
                s.Driver.Navigate().GoToUrl(s.Link("/server/emails"));
                if (s.Driver.PageSource.Contains("Configured"))
                {
                    s.Driver.FindElement(By.CssSelector("button[value=\"ResetPassword\"]")).Submit();
                    s.AssertHappyMessage();
                }
                CanSetupEmailCore(s);
                s.CreateNewStore();
                s.GoToUrl($"stores/{s.StoreId}/emails");
                CanSetupEmailCore(s);
            }
        }

        private static void CanSetupEmailCore(SeleniumTester s)
        {
            s.Driver.FindElement(By.ClassName("dropdown-toggle")).Click();
            s.Driver.FindElement(By.ClassName("dropdown-item")).Click();
            s.Driver.FindElement(By.Id("Settings_Login")).SendKeys("test@gmail.com");
            s.Driver.FindElement(By.CssSelector("button[value=\"Save\"]")).Submit();
            s.AssertHappyMessage();
            s.Driver.FindElement(By.Id("Settings_Password")).SendKeys("mypassword");
            s.Driver.FindElement(By.CssSelector("button[value=\"Save\"]")).Submit();
            Assert.Contains("Configured", s.Driver.PageSource);
            s.Driver.FindElement(By.Id("Settings_Login")).SendKeys("test_fix@gmail.com");
            s.Driver.FindElement(By.CssSelector("button[value=\"Save\"]")).Submit();
            Assert.Contains("Configured", s.Driver.PageSource);
            Assert.Contains("test_fix", s.Driver.PageSource);
            s.Driver.FindElement(By.CssSelector("button[value=\"ResetPassword\"]")).Submit();
            s.AssertHappyMessage();
            Assert.DoesNotContain("Configured", s.Driver.PageSource);
            Assert.Contains("test_fix", s.Driver.PageSource);
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
                var invoiceId = s.CreateInvoice(store);
                s.AssertHappyMessage();
                s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
                var invoiceUrl = s.Driver.Url;

                //let's test archiving an invoice
                Assert.DoesNotContain("Archived", s.Driver.FindElement(By.Id("btn-archive-toggle")).Text);
                s.Driver.FindElement(By.Id("btn-archive-toggle")).Click();
                s.AssertHappyMessage();
                Assert.Contains("Archived", s.Driver.FindElement(By.Id("btn-archive-toggle")).Text);
                //check that it no longer appears in list
                s.GoToInvoices();
                Assert.DoesNotContain(invoiceId, s.Driver.PageSource);
                //ok, let's unarchive and see that it shows again
                s.Driver.Navigate().GoToUrl(invoiceUrl);
                s.Driver.FindElement(By.Id("btn-archive-toggle")).Click();
                s.AssertHappyMessage();
                Assert.DoesNotContain("Archived", s.Driver.FindElement(By.Id("btn-archive-toggle")).Text);
                s.GoToInvoices();
                Assert.Contains(invoiceId, s.Driver.PageSource);

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
                s.Driver.FindElement(By.Id("DefaultView")).SendKeys("Cart" + Keys.Enter);
                s.Driver.FindElement(By.Id("SaveSettings")).ForceClick();
                s.Driver.FindElement(By.Id("ViewApp")).ForceClick();

                var posBaseUrl = s.Driver.Url.Replace("/Cart", "");
                Assert.True(s.Driver.PageSource.Contains("Tea shop"), "Unable to create PoS");
                Assert.True(s.Driver.PageSource.Contains("Cart"), "PoS not showing correct default view");

                s.Driver.Url = posBaseUrl + "/static";
                Assert.False(s.Driver.PageSource.Contains("Cart"), "Static PoS not showing correct view");

                s.Driver.Url = posBaseUrl + "/cart";
                Assert.True(s.Driver.PageSource.Contains("Cart"), "Cart PoS not showing correct view");

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
        public async Task CanUseCoinSelection()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                var userId = s.RegisterNewUser(true);
                var storeId = s.CreateNewStore().storeId;
                s.GenerateWallet("BTC", "", false, true);
                var walletId = new WalletId(storeId, "BTC");
                s.GoToWallet(walletId, WalletsNavPages.Receive);
                s.Driver.FindElement(By.Id("generateButton")).Click();
                var addressStr = s.Driver.FindElement(By.Id("vue-address")).GetProperty("value");
                var address = BitcoinAddress.Create(addressStr, ((BTCPayNetwork)s.Server.NetworkProvider.GetNetwork("BTC")).NBitcoinNetwork);
                await s.Server.ExplorerNode.GenerateAsync(1);
                for (int i = 0; i < 6; i++)
                {
                    await s.Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(1.0m));
                }
                var targetTx = await s.Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(1.2m));
                var tx = await s.Server.ExplorerNode.GetRawTransactionAsync(targetTx);
                var spentOutpoint = new OutPoint(targetTx, tx.Outputs.FindIndex(txout => txout.Value == Money.Coins(1.2m)));
                await TestUtils.EventuallyAsync(async () =>
                {
                    var store = await s.Server.PayTester.StoreRepository.FindStore(storeId);
                    var x = store.GetSupportedPaymentMethods(s.Server.NetworkProvider)
                        .OfType<DerivationSchemeSettings>()
                        .Single(settings => settings.PaymentId.CryptoCode == walletId.CryptoCode);
                    Assert.Contains(
                        await s.Server.PayTester.GetService<BTCPayWalletProvider>().GetWallet(walletId.CryptoCode)
                            .GetUnspentCoins(x.AccountDerivation),
                        coin => coin.OutPoint == spentOutpoint);
                });
                await s.Server.ExplorerNode.GenerateAsync(1);
                s.GoToWallet(walletId, WalletsNavPages.Send);
                s.Driver.FindElement(By.Id("advancedSettings")).Click();
                s.Driver.FindElement(By.Id("toggleInputSelection")).Click();
                s.Driver.WaitForElement(By.Id(spentOutpoint.ToString()));
                Assert.Equal("true", s.Driver.FindElement(By.Name("InputSelection")).GetAttribute("value").ToLowerInvariant());
                var el = s.Driver.FindElement(By.Id(spentOutpoint.ToString()));
                s.Driver.FindElement(By.Id(spentOutpoint.ToString())).Click();
                var inputSelectionSelect = s.Driver.FindElement(By.Name("SelectedInputs"));
                Assert.Single(inputSelectionSelect.FindElements(By.CssSelector("[selected]")));

                var bob = new Key().PubKey.Hash.GetAddress(Network.RegTest);
                SetTransactionOutput(s, 0, bob, 0.3m);
                s.Driver.FindElement(By.Id("SendMenu")).Click();
                s.Driver.FindElement(By.Id("spendWithNBxplorer")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).ForceClick();
                var happyElement = s.AssertHappyMessage();
                var happyText = happyElement.Text;
                var txid = Regex.Match(happyText, @"\((.*)\)").Groups[1].Value;

                tx = await s.Server.ExplorerNode.GetRawTransactionAsync(new uint256(txid));
                Assert.Single(tx.Inputs);
                Assert.Equal(spentOutpoint, tx.Inputs[0].PrevOut);
            }
        }


        [Fact(Timeout = TestTimeout)]
        public async Task CanManageWallet()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser(true);
                var storeId = s.CreateNewStore();

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
                Assert.Equal(receiveAddr, s.Driver.FindElement(By.Id("vue-address")).GetAttribute("value"));

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
                Assert.NotEqual(receiveAddr, s.Driver.FindElement(By.Id("vue-address")).GetAttribute("value"));
                receiveAddr = s.Driver.FindElement(By.Id("vue-address")).GetAttribute("value");

                //change the wallet and ensure old address is not there and generating a new one does not result in the prev one
                s.GoToStore(storeId.storeId);
                s.GenerateWallet("BTC", "", true, false);
                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();
                s.Driver.FindElement(By.Id("WalletReceive")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
                Assert.NotEqual(receiveAddr, s.Driver.FindElement(By.Id("vue-address")).GetAttribute("value"));

                var invoiceId = s.CreateInvoice(storeId.storeName);
                var invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
                var address = invoice.EntityToDTO().Addresses["BTC"];

                //wallet should have been imported to bitcoin core wallet in watch only mode.
                var result = await s.Server.ExplorerNode.GetAddressInfoAsync(BitcoinAddress.Create(address, Network.RegTest));
                Assert.True(result.IsWatchOnly);
                s.GoToStore(storeId.storeId);
                var mnemonic = s.GenerateWallet("BTC", "", true, true);

                //lets import and save private keys
                var root = mnemonic.DeriveExtKey();
                invoiceId = s.CreateInvoice(storeId.storeName);
                invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
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


                void SignWith(Mnemonic signingSource)
                {
                    // Send to bob
                    s.Driver.FindElement(By.Id("WalletSend")).Click();
                    var bob = new Key().PubKey.Hash.GetAddress(Network.RegTest);
                    SetTransactionOutput(s, 0, bob, 1);
                    s.Driver.ScrollTo(By.Id("SendMenu"));
                    s.Driver.FindElement(By.Id("SendMenu")).ForceClick();
                    s.Driver.FindElement(By.CssSelector("button[value=seed]")).Click();

                    // Input the seed
                    s.Driver.FindElement(By.Id("SeedOrKey")).SendKeys(signingSource.ToString() + Keys.Enter);

                    // Broadcast
                    Assert.Contains(bob.ToString(), s.Driver.PageSource);
                    Assert.Contains("1.00000000", s.Driver.PageSource);
                    s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).ForceClick();
                    Assert.Equal(walletTransactionLink, s.Driver.Url);
                }

                SignWith(mnemonic);

                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();
                s.Driver.FindElement(By.Id("WalletSend")).Click();

                var jack = new Key().PubKey.Hash.GetAddress(Network.RegTest);
                SetTransactionOutput(s, 0, jack, 0.01m);
                s.Driver.ScrollTo(By.Id("SendMenu"));
                s.Driver.FindElement(By.Id("SendMenu")).ForceClick();

                s.Driver.FindElement(By.CssSelector("button[value=nbx-seed]")).Click();
                Assert.Contains(jack.ToString(), s.Driver.PageSource);
                Assert.Contains("0.01000000", s.Driver.PageSource);
                s.Driver.FindElement(By.CssSelector("button[value=analyze-psbt]")).ForceClick();
                Assert.EndsWith("psbt", s.Driver.Url);
                s.Driver.FindElement(By.CssSelector("#OtherActions")).ForceClick();
                s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).ForceClick();
                Assert.EndsWith("psbt/ready", s.Driver.Url);
                s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).ForceClick();
                Assert.Equal(walletTransactionLink, s.Driver.Url);

                var bip21 = invoice.EntityToDTO().CryptoInfo.First().PaymentUrls.BIP21;
                //let's make bip21 more interesting
                bip21 += "&label=Solid Snake&message=Snake? Snake? SNAAAAKE!";
                var parsedBip21 = new BitcoinUrlBuilder(bip21, Network.RegTest);
                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();
                s.Driver.FindElement(By.Id("WalletSend")).Click();
                s.Driver.FindElement(By.Id("bip21parse")).Click();
                s.Driver.SwitchTo().Alert().SendKeys(bip21);
                s.Driver.SwitchTo().Alert().Accept();
                s.AssertHappyMessage(StatusMessageModel.StatusSeverity.Info);
                Assert.Equal(parsedBip21.Amount.ToString(false), s.Driver.FindElement(By.Id($"Outputs_0__Amount")).GetAttribute("value"));
                Assert.Equal(parsedBip21.Address.ToString(), s.Driver.FindElement(By.Id($"Outputs_0__DestinationAddress")).GetAttribute("value"));

                s.GoToWallet(new WalletId(storeId.storeId, "BTC"), WalletsNavPages.Settings);
                var walletUrl = s.Driver.Url;

                s.Driver.FindElement(By.Id("SettingsMenu")).ForceClick();
                s.Driver.FindElement(By.CssSelector("button[value=view-seed]")).Click();

                // Seed backup page
                var recoveryPhrase = s.Driver.FindElements(By.Id("recovery-phrase")).First().GetAttribute("data-mnemonic");
                Assert.Equal(mnemonic.ToString(), recoveryPhrase);
                Assert.Contains("The recovery phrase will also be stored on the server as a hot wallet.", s.Driver.PageSource);

                // No confirmation, just a link to return to the wallet
                Assert.Empty(s.Driver.FindElements(By.Id("confirm")));
                s.Driver.FindElement(By.Id("proceed")).Click();
                Assert.Equal(walletUrl, s.Driver.Url);
            }
        }
        void SetTransactionOutput(SeleniumTester s, int index, BitcoinAddress dest, decimal amount, bool subtract = false)
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

        [Fact]
        [Trait("Selenium", "Selenium")]
        public async Task CanUsePullPaymentsViaUI()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser(true);
                var receiver = s.CreateNewStore();
                var receiverSeed = s.GenerateWallet("BTC", "", true, true, ScriptPubKeyType.Segwit);
                await s.Server.ExplorerNode.GenerateAsync(1);
                await s.FundStoreWallet(denomination: 50.0m);
                s.GoToWallet(navPages: WalletsNavPages.PullPayments);
                s.Driver.FindElement(By.Id("NewPullPayment")).Click();
                s.Driver.FindElement(By.Id("Name")).SendKeys("PP1");
                s.Driver.FindElement(By.Id("Amount")).Clear();
                s.Driver.FindElement(By.Id("Amount")).SendKeys("99.0" + Keys.Enter);
                s.Driver.FindElement(By.LinkText("View")).Click();

                Thread.Sleep(1000);
                s.GoToWallet(navPages: WalletsNavPages.PullPayments);
                s.Driver.FindElement(By.Id("NewPullPayment")).Click();
                s.Driver.FindElement(By.Id("Name")).SendKeys("PP2");
                s.Driver.FindElement(By.Id("Amount")).Clear();
                s.Driver.FindElement(By.Id("Amount")).SendKeys("100.0" + Keys.Enter);
                // This should select the first View, ie, the last one PP2
                s.Driver.FindElement(By.LinkText("View")).Click();

                Thread.Sleep(1000);
                var address = await s.Server.ExplorerNode.GetNewAddressAsync();
                s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
                s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
                s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("15" + Keys.Enter);
                s.AssertHappyMessage();

                // We should not be able to use an address already used
                s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
                s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
                s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("20" + Keys.Enter);
                s.AssertHappyMessage(StatusMessageModel.StatusSeverity.Error);

                address = await s.Server.ExplorerNode.GetNewAddressAsync();
                s.Driver.FindElement(By.Id("Destination")).Clear();
                s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
                s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
                s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("20" + Keys.Enter);
                s.AssertHappyMessage();
                Assert.Contains("Awaiting Approval", s.Driver.PageSource);

                var viewPullPaymentUrl = s.Driver.Url;
                // This one should have nothing
                s.GoToWallet(navPages: WalletsNavPages.PullPayments);
                var payouts = s.Driver.FindElements(By.ClassName("pp-payout"));
                Assert.Equal(2, payouts.Count);
                payouts[1].Click();
                Assert.Contains("No payout waiting for approval", s.Driver.PageSource);

                // PP2 should have payouts
                s.GoToWallet(navPages: WalletsNavPages.PullPayments);
                payouts = s.Driver.FindElements(By.ClassName("pp-payout"));
                payouts[0].Click();
                Assert.DoesNotContain("No payout waiting for approval", s.Driver.PageSource);
                s.Driver.FindElement(By.Id("selectAllCheckbox")).Click();
                s.Driver.FindElement(By.Id("payCommand")).Click();
                s.Driver.ScrollTo(By.Id("SendMenu"));
                s.Driver.FindElement(By.Id("SendMenu")).ForceClick();
                s.Driver.FindElement(By.CssSelector("button[value=nbx-seed]")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).ForceClick();
                s.AssertHappyMessage();

                TestUtils.Eventually(() =>
                {
                    s.Driver.Navigate().Refresh();
                    Assert.Contains("badge transactionLabel", s.Driver.PageSource);
                });
                Assert.Equal("payout", s.Driver.FindElement(By.ClassName("transactionLabel")).Text);

                s.GoToWallet(navPages: WalletsNavPages.Payouts);
                TestUtils.Eventually(() =>
                {
                    s.Driver.Navigate().Refresh();
                    Assert.Contains("No payout waiting for approval", s.Driver.PageSource);
                });
                var txs = s.Driver.FindElements(By.ClassName("transaction-link"));
                Assert.Equal(2, txs.Count);

                s.Driver.Navigate().GoToUrl(viewPullPaymentUrl);
                txs = s.Driver.FindElements(By.ClassName("transaction-link"));
                Assert.Equal(2, txs.Count);
                Assert.Contains("In Progress", s.Driver.PageSource);

                await s.Server.ExplorerNode.GenerateAsync(1);

                TestUtils.Eventually(() =>
                {
                    s.Driver.Navigate().Refresh();
                    Assert.Contains("Completed", s.Driver.PageSource);
                });
                await s.Server.ExplorerNode.GenerateAsync(10);
                var pullPaymentId = viewPullPaymentUrl.Split('/').Last();

                await TestUtils.EventuallyAsync(async () =>
                {
                    using var ctx = s.Server.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();
                    var payoutsData = await ctx.Payouts.Where(p => p.PullPaymentDataId == pullPaymentId).ToListAsync();
                    Assert.True(payoutsData.All(p => p.State == Data.PayoutState.Completed));
                });
            }
        }
    }
}
