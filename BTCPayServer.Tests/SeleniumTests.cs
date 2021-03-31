using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;
using OpenQA.Selenium.Support.UI;
using Renci.SshNet.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    public class ChromeTests
    {
        private const int TestTimeout = TestUtils.TestTimeout;

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
                s.FindAlertMessage();
                seedEl = s.Driver.FindElement(By.Id("SeedTextArea"));
                Assert.Contains("Seed removed", seedEl.Text, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Selenium", "Selenium")]
        public async Task CanChangeUserMail()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();

                var tester = s.Server;
                var u1 = tester.NewAccount();
                u1.GrantAccess();
                await u1.MakeAdmin(false);

                var u2 = tester.NewAccount();
                u2.GrantAccess();
                await u2.MakeAdmin(false);

                s.GoToLogin();
                s.Login(u1.RegisterDetails.Email, u1.RegisterDetails.Password);
                s.GoToProfile(ManageNavPages.Index);
                s.Driver.FindElement(By.Id("Email")).Clear();
                s.Driver.FindElement(By.Id("Email")).SendKeys(u2.RegisterDetails.Email);
                s.Driver.FindElement(By.Id("save")).Click();

                s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);

                s.GoToProfile(ManageNavPages.Index);
                s.Driver.FindElement(By.Id("Email")).Clear();
                var changedEmail = Guid.NewGuid() + "@lol.com";
                s.Driver.FindElement(By.Id("Email")).SendKeys(changedEmail);
                s.Driver.FindElement(By.Id("save")).Click();
                s.FindAlertMessage(StatusMessageModel.StatusSeverity.Success);

                var manager = tester.PayTester.GetService<UserManager<ApplicationUser>>();
                Assert.NotNull(await manager.FindByNameAsync(changedEmail));
                Assert.NotNull(await manager.FindByEmailAsync(changedEmail));
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
                Assert.Contains("/login", s.Driver.Url);

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
                s.RegisterNewUser(true);
                s.GoToServer(ServerNavPages.Users);
                s.Driver.FindElement(By.Id("CreateUser")).Click();

                var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
                s.Driver.FindElement(By.Id("Email")).SendKeys(usr);
                s.Driver.FindElement(By.Id("Save")).Click();
                var url = s.FindAlertMessage().FindElement(By.TagName("a")).Text;

                s.Logout();
                s.Driver.Navigate().GoToUrl(url);
                Assert.Equal("hidden", s.Driver.FindElement(By.Id("Email")).GetAttribute("type"));
                Assert.Equal(usr, s.Driver.FindElement(By.Id("Email")).GetAttribute("value"));

                s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
                s.Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("123456");
                s.Driver.FindElement(By.Id("SetPassword")).Click();
                s.FindAlertMessage();
                s.Driver.FindElement(By.Id("Email")).SendKeys(usr);
                s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
                s.Driver.FindElement(By.Id("LoginButton")).Click();

                // We should be logged in now
                s.Driver.FindElement(By.Id("mainNav"));
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseSSHService()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser(isAdmin: true);
                s.Driver.Navigate().GoToUrl(s.Link("/server/services"));
                Assert.Contains("server/services/ssh", s.Driver.PageSource);
                using (var client = await s.Server.PayTester.GetService<Configuration.BTCPayServerOptions>().SSHSettings.ConnectAsync())
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
                s.Driver.FindElement(By.Id("submit")).Click();
                s.Driver.AssertNoError();

                var text = s.Driver.FindElement(By.Id("SSHKeyFileContent")).Text;
                // Browser replace \n to \r\n, so it is hard to compare exactly what we want
                Assert.Contains("tes't", text);
                Assert.Contains("test2", text);
                Assert.True(s.Driver.PageSource.Contains("authorized_keys has been updated", StringComparison.OrdinalIgnoreCase));

                s.Driver.FindElement(By.Id("SSHKeyFileContent")).Clear();
                s.Driver.FindElement(By.Id("submit")).Click();

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
                s.RegisterNewUser(isAdmin: true);
                s.Driver.Navigate().GoToUrl(s.Link("/server/emails"));
                if (s.Driver.PageSource.Contains("Configured"))
                {
                    s.Driver.FindElement(By.CssSelector("button[value=\"ResetPassword\"]")).Submit();
                    s.FindAlertMessage();
                }
                CanSetupEmailCore(s);
                s.CreateNewStore();
                s.GoToUrl($"stores/{s.StoreId}/emails");
                CanSetupEmailCore(s);
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseDynamicDns()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser(isAdmin: true);
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
        [Trait("Lightning", "Lightning")]
        public async Task CanCreateStores()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Server.ActivateLightning();
                await s.StartAsync();
                var alice = s.RegisterNewUser(true);
                var (storeName, storeId) = s.CreateNewStore();
                var onchainHint = "Set up your wallet to receive payments at your store.";
                var offchainHint = "A connection to a Lightning node is required to receive Lightning payments.";

                // verify that hints are displayed on the store page
                Assert.True(s.Driver.PageSource.Contains(onchainHint), "Wallet hint not present");
                Assert.True(s.Driver.PageSource.Contains(offchainHint), "Lightning hint not present");

                s.GoToStores();
                Assert.True(s.Driver.PageSource.Contains($"warninghint_{storeId}"), "Warning hint on list not present");

                s.GoToStore(storeId);
                Assert.Contains(storeName, s.Driver.PageSource);
                Assert.True(s.Driver.PageSource.Contains(onchainHint), "Wallet hint should be present at this point");
                Assert.True(s.Driver.PageSource.Contains(offchainHint), "Lightning hint should be present at this point");

                // setup onchain wallet
                s.GoToStore(storeId);
                s.AddDerivationScheme();
                s.Driver.AssertNoError();
                Assert.False(s.Driver.PageSource.Contains(onchainHint), "Wallet hint not dismissed on derivation scheme add");

                // setup offchain wallet
                s.GoToStore(storeId);
                s.AddLightningNode();
                s.Driver.AssertNoError();
                var successAlert = s.FindAlertMessage();
                Assert.Contains("BTC Lightning node modified.", successAlert.Text);
                Assert.False(s.Driver.PageSource.Contains(offchainHint), "Lightning hint should be dismissed at this point");

                var storeUrl = s.Driver.Url;
                s.ClickOnAllSideMenus();
                s.GoToInvoices();
                var invoiceId = s.CreateInvoice(storeName);
                s.FindAlertMessage();
                s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
                var invoiceUrl = s.Driver.Url;

                //let's test archiving an invoice
                Assert.DoesNotContain("Archived", s.Driver.FindElement(By.Id("btn-archive-toggle")).Text);
                s.Driver.FindElement(By.Id("btn-archive-toggle")).Click();
                Assert.Contains("Archived", s.Driver.FindElement(By.Id("btn-archive-toggle")).Text);
                //check that it no longer appears in list
                s.GoToInvoices();

                Assert.DoesNotContain(invoiceId, s.Driver.PageSource);
                //ok, let's unarchive and see that it shows again
                s.Driver.Navigate().GoToUrl(invoiceUrl);
                s.Driver.FindElement(By.Id("btn-archive-toggle")).Click();
                s.FindAlertMessage();
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
                s.LogIn(alice);
                s.Driver.Navigate().GoToUrl(storeUrl + "/users");
                s.Driver.FindElement(By.Id("Email")).SendKeys(bob + Keys.Enter);
                Assert.Contains("User added successfully", s.Driver.PageSource);
                s.Logout();

                // Bob should not have access to store, but should have access to invoice
                s.LogIn(bob);
                s.Driver.Navigate().GoToUrl(storeUrl);
                Assert.Contains("ReturnUrl", s.Driver.Url);
                s.Driver.Navigate().GoToUrl(invoiceUrl);
                s.Driver.AssertNoError();

                // Alice should be able to delete the store
                s.Logout();
                s.LogIn(alice);
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
                s.RegisterNewUser();
                s.CreateNewStore();
                s.AddDerivationScheme();

                s.Driver.FindElement(By.Id("Tokens")).Click();
                s.Driver.FindElement(By.Id("CreateNewToken")).Click();
                s.Driver.FindElement(By.Id("RequestPairing")).Click();
                var pairingCode = AssertUrlHasPairingCode(s);

                s.Driver.FindElement(By.Id("ApprovePairing")).Click();
                s.FindAlertMessage();
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

        [Fact(Timeout = TestTimeout)]
        public async Task CanCreateAppPoS()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser();
                var (storeName, _) = s.CreateNewStore();

                s.Driver.FindElement(By.Id("Apps")).Click();
                s.Driver.FindElement(By.Id("CreateNewApp")).Click();
                s.Driver.FindElement(By.Name("Name")).SendKeys("PoS" + Guid.NewGuid());
                s.Driver.FindElement(By.Id("SelectedAppType")).SendKeys("PointOfSale");
                s.Driver.FindElement(By.Id("SelectedStore")).SendKeys(storeName);
                s.Driver.FindElement(By.Id("Create")).Click();
                s.Driver.FindElement(By.Id("DefaultView")).SendKeys("Cart");
                s.Driver.FindElement(By.CssSelector(".template-item:nth-of-type(1) .btn-primary")).Click();
                s.Driver.FindElement(By.Id("BuyButtonText")).SendKeys("Take my money");
                s.Driver.FindElement(By.Id("SaveItemChanges")).Click();
                s.Driver.FindElement(By.Id("ToggleRawEditor")).Click();

                var template = s.Driver.FindElement(By.Id("Template")).GetAttribute("value");
                Assert.Contains("buyButtonText: Take my money", template);

                s.Driver.FindElement(By.Id("SaveSettings")).Click();
                s.Driver.FindElement(By.Id("ViewApp")).Click();

                var posBaseUrl = s.Driver.Url.Replace("/Cart", "");
                Assert.True(s.Driver.PageSource.Contains("Tea shop"), "Unable to create PoS");
                Assert.True(s.Driver.PageSource.Contains("Cart"), "PoS not showing correct default view");
                Assert.True(s.Driver.PageSource.Contains("Take my money"), "PoS not showing correct default view");

                s.Driver.Url = posBaseUrl + "/static";
                Assert.False(s.Driver.PageSource.Contains("Cart"), "Static PoS not showing correct view");

                s.Driver.Url = posBaseUrl + "/cart";
                Assert.True(s.Driver.PageSource.Contains("Cart"), "Cart PoS not showing correct view");
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanCreateCrowdfundingApp()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser();
                var (storeName, _) = s.CreateNewStore();
                s.AddDerivationScheme();

                s.Driver.FindElement(By.Id("Apps")).Click();
                s.Driver.FindElement(By.Id("CreateNewApp")).Click();
                s.Driver.FindElement(By.Name("Name")).SendKeys("CF" + Guid.NewGuid());
                s.Driver.FindElement(By.Id("SelectedAppType")).SendKeys("Crowdfund");
                s.Driver.FindElement(By.Id("SelectedStore")).SendKeys(storeName);
                s.Driver.FindElement(By.Id("Create")).Click();
                s.Driver.FindElement(By.Id("Title")).SendKeys("Kukkstarter");
                s.Driver.FindElement(By.CssSelector("div.note-editable.card-block")).SendKeys("1BTC = 1BTC");
                s.Driver.FindElement(By.Id("TargetCurrency")).SendKeys("JPY");
                s.Driver.FindElement(By.Id("TargetAmount")).SendKeys("700");
                s.Driver.FindElement(By.Id("SaveSettings")).Click();
                s.Driver.FindElement(By.Id("ViewApp")).Click();
                Assert.Equal("Currently Active!", s.Driver.FindElement(By.CssSelector(".h6.text-muted")).Text);
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
                s.Driver.FindElement(By.Id("SaveButton")).Click();
                s.Driver.FindElement(By.Name("ViewAppButton")).Click();
                s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
                Assert.Equal("Amount due", s.Driver.FindElement(By.CssSelector("[data-test='amount-due-title']")).Text);
            }
        }


        [Fact(Timeout = TestTimeout)]
        public async Task CanUseCoinSelection()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser(true);
                var (_, storeId) = s.CreateNewStore();
                s.GenerateWallet("BTC", "", false, true);
                var walletId = new WalletId(storeId, "BTC");
                s.GoToWallet(walletId, WalletsNavPages.Receive);
                s.Driver.FindElement(By.Id("generateButton")).Click();
                var addressStr = s.Driver.FindElement(By.Id("address")).GetProperty("value");
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
                    var wallet = s.Server.PayTester.GetService<BTCPayWalletProvider>().GetWallet(walletId.CryptoCode);
                    wallet.InvalidateCache(x.AccountDerivation);
                    Assert.Contains(
                        await wallet.GetUnspentCoins(x.AccountDerivation),
                        coin => coin.OutPoint == spentOutpoint);
                });
                await s.Server.ExplorerNode.GenerateAsync(1);
                s.GoToWallet(walletId);
                s.Driver.FindElement(By.Id("advancedSettings")).Click();
                s.Driver.WaitForAndClick(By.Id("toggleInputSelection"));
                s.Driver.FindElement(By.Id(spentOutpoint.ToString()));
                Assert.Equal("true", s.Driver.FindElement(By.Name("InputSelection")).GetAttribute("value").ToLowerInvariant());
                var el = s.Driver.FindElement(By.Id(spentOutpoint.ToString()));
                s.Driver.FindElement(By.Id(spentOutpoint.ToString())).Click();
                var inputSelectionSelect = s.Driver.FindElement(By.Name("SelectedInputs"));
                Assert.Single(inputSelectionSelect.FindElements(By.CssSelector("[selected]")));

                var bob = new Key().PubKey.Hash.GetAddress(Network.RegTest);
                SetTransactionOutput(s, 0, bob, 0.3m);
                s.Driver.FindElement(By.Id("SendMenu")).Click();
                s.Driver.FindElement(By.Id("spendWithNBxplorer")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
                var happyElement = s.FindAlertMessage();
                var happyText = happyElement.Text;
                var txid = Regex.Match(happyText, @"\((.*)\)").Groups[1].Value;

                tx = await s.Server.ExplorerNode.GetRawTransactionAsync(new uint256(txid));
                Assert.Single(tx.Inputs);
                Assert.Equal(spentOutpoint, tx.Inputs[0].PrevOut);
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseWebhooks()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser(true);
                var (storeName, storeId) = s.CreateNewStore();
                s.GoToStore(storeId, Views.Stores.StoreNavPages.Webhooks);

                Logs.Tester.LogInformation("Let's create two webhooks");
                for (var i = 0; i < 2; i++)
                {
                    s.Driver.FindElement(By.Id("CreateWebhook")).Click();
                    s.Driver.FindElement(By.Name("PayloadUrl")).SendKeys($"http://127.0.0.1/callback{i}");
                    new SelectElement(s.Driver.FindElement(By.Name("Everything")))
                        .SelectByValue("false");
                    s.Driver.FindElement(By.Id("InvoiceCreated")).Click();
                    s.Driver.FindElement(By.Id("InvoiceProcessing")).Click();
                    s.Driver.FindElement(By.Name("add")).Click();
                }

                Logs.Tester.LogInformation("Let's delete one of them");
                var deletes = s.Driver.FindElements(By.LinkText("Delete"));
                Assert.Equal(2, deletes.Count);
                deletes[0].Click();
                s.Driver.FindElement(By.Id("continue")).Click();
                deletes = s.Driver.FindElements(By.LinkText("Delete"));
                Assert.Single(deletes);
                s.FindAlertMessage();

                Logs.Tester.LogInformation("Let's try to update one of them");
                s.Driver.FindElement(By.LinkText("Modify")).Click();

                using FakeServer server = new FakeServer();
                await server.Start();
                s.Driver.FindElement(By.Name("PayloadUrl")).Clear();
                s.Driver.FindElement(By.Name("PayloadUrl")).SendKeys(server.ServerUri.AbsoluteUri);
                s.Driver.FindElement(By.Name("Secret")).Clear();
                s.Driver.FindElement(By.Name("Secret")).SendKeys("HelloWorld");
                s.Driver.FindElement(By.Name("update")).Click();
                s.FindAlertMessage();
                s.Driver.FindElement(By.LinkText("Modify")).Click();
                foreach (var value in Enum.GetValues(typeof(WebhookEventType)))
                {
                    // Here we make sure we did not forget an event type in the list
                    // However, maybe some event should not appear here because not at the store level.
                    // Fix as needed.
                    Assert.Contains($"value=\"{value}\"", s.Driver.PageSource);
                }
                // This one should be checked
                Assert.Contains($"value=\"InvoiceProcessing\" checked", s.Driver.PageSource);
                Assert.Contains($"value=\"InvoiceCreated\" checked", s.Driver.PageSource);
                // This one never been checked
                Assert.DoesNotContain($"value=\"InvoiceReceivedPayment\" checked", s.Driver.PageSource);

                s.Driver.FindElement(By.Name("update")).Click();
                s.FindAlertMessage();
                Assert.Contains(server.ServerUri.AbsoluteUri, s.Driver.PageSource);

                Logs.Tester.LogInformation("Let's see if we can generate an event");
                s.GoToStore(storeId);
                s.AddDerivationScheme();
                s.CreateInvoice(storeName);
                var request = await server.GetNextRequest();
                var headers = request.Request.Headers;
                var actualSig = headers["BTCPay-Sig"].First();
                var bytes = await request.Request.Body.ReadBytesAsync((int)headers.ContentLength.Value);
                var expectedSig = $"sha256={Encoders.Hex.EncodeData(new HMACSHA256(Encoding.UTF8.GetBytes("HelloWorld")).ComputeHash(bytes))}";
                Assert.Equal(expectedSig, actualSig);
                request.Response.StatusCode = 200;
                server.Done();

                Logs.Tester.LogInformation("Let's make a failed event");
                s.CreateInvoice(storeName);
                request = await server.GetNextRequest();
                request.Response.StatusCode = 404;
                server.Done();

                // The delivery is done asynchronously, so small wait here
                await Task.Delay(500);
                s.GoToStore(storeId, Views.Stores.StoreNavPages.Webhooks);
                s.Driver.FindElement(By.LinkText("Modify")).Click();
                var elements = s.Driver.FindElements(By.ClassName("redeliver"));
                // One worked, one failed
                s.Driver.FindElement(By.ClassName("fa-times"));
                s.Driver.FindElement(By.ClassName("fa-check"));
                elements[0].Click();

                s.FindAlertMessage();
                request = await server.GetNextRequest();
                request.Response.StatusCode = 404;
                server.Done();

                Logs.Tester.LogInformation("Can we browse the json content?");
                CanBrowseContent(s);

                s.GoToInvoices();
                s.Driver.FindElement(By.LinkText("Details")).Click();
                CanBrowseContent(s);
                var element = s.Driver.FindElement(By.ClassName("redeliver"));
                element.Click();

                s.FindAlertMessage();
                request = await server.GetNextRequest();
                request.Response.StatusCode = 404;
                server.Done();

                Logs.Tester.LogInformation("Let's see if we can delete store with some webhooks inside");
                s.GoToStore(storeId);
                // Open danger zone via JS, because if we click the link it triggers the toggle animation.
                // This leads to Selenium trying to click the button while it is moving resulting in an error.
                s.Driver.ExecuteJavaScript("document.getElementById('danger-zone').classList.add('show')");
                s.Driver.FindElement(By.Id("delete-store")).Click();
                s.Driver.FindElement(By.Id("continue")).Click();
                s.FindAlertMessage();
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanManageWallet()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                s.RegisterNewUser(true);
                var (storeName, storeId) = s.CreateNewStore();

                // In this test, we try to spend from a manual seed. We import the xpub 49'/0'/0',
                // then try to use the seed to sign the transaction
                s.GenerateWallet("BTC", "", true);

                //let's test quickly the receive wallet page
                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();
                s.Driver.FindElement(By.Id("WalletSend")).Click();
                s.Driver.FindElement(By.Id("SendMenu")).Click();

                //you cannot use the Sign with NBX option without saving private keys when generating the wallet.
                Assert.DoesNotContain("nbx-seed", s.Driver.PageSource);

                s.Driver.FindElement(By.Id("WalletReceive")).Click();
                //generate a receiving address
                s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
                Assert.True(s.Driver.FindElement(By.ClassName("qr-container")).Displayed);
                var receiveAddr = s.Driver.FindElement(By.Id("address")).GetAttribute("value");
                //unreserve
                s.Driver.FindElement(By.CssSelector("button[value=unreserve-current-address]")).Click();
                //generate it again, should be the same one as before as nothing got used in the meantime
                s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
                Assert.True(s.Driver.FindElement(By.ClassName("qr-container")).Displayed);
                Assert.Equal(receiveAddr, s.Driver.FindElement(By.Id("address")).GetAttribute("value"));

                //send money to addr and ensure it changed
                var sess = await s.Server.ExplorerClient.CreateWebsocketNotificationSessionAsync();
                await sess.ListenAllTrackedSourceAsync();
                var nextEvent = sess.NextEventAsync();
                await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(receiveAddr, Network.RegTest), Money.Parse("0.1"));
                await nextEvent;
                await Task.Delay(200);
                s.Driver.Navigate().Refresh();
                s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();
                Assert.NotEqual(receiveAddr, s.Driver.FindElement(By.Id("address")).GetAttribute("value"));
                receiveAddr = s.Driver.FindElement(By.Id("address")).GetAttribute("value");

                //change the wallet and ensure old address is not there and generating a new one does not result in the prev one
                s.GoToStore(storeId);
                s.GenerateWallet("BTC", "", true);
                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();
                s.Driver.FindElement(By.Id("WalletReceive")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=generate-new-address]")).Click();

                Assert.NotEqual(receiveAddr, s.Driver.FindElement(By.Id("address")).GetAttribute("value"));

                var invoiceId = s.CreateInvoice(storeName);
                var invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
                var address = invoice.EntityToDTO().Addresses["BTC"];

                //wallet should have been imported to bitcoin core wallet in watch only mode.
                var result = await s.Server.ExplorerNode.GetAddressInfoAsync(BitcoinAddress.Create(address, Network.RegTest));
                Assert.True(result.IsWatchOnly);
                s.GoToStore(storeId);
                var mnemonic = s.GenerateWallet("BTC", "", true, true);

                //lets import and save private keys
                var root = mnemonic.DeriveExtKey();
                invoiceId = s.CreateInvoice(storeName);
                invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
                address = invoice.EntityToDTO().Addresses["BTC"];
                result = await s.Server.ExplorerNode.GetAddressInfoAsync(BitcoinAddress.Create(address, Network.RegTest));
                //spendable from bitcoin core wallet!
                Assert.False(result.IsWatchOnly);
                var tx = s.Server.ExplorerNode.SendToAddress(BitcoinAddress.Create(address, Network.RegTest), Money.Coins(3.0m));
                await s.Server.ExplorerNode.GenerateAsync(1);

                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();

                s.ClickOnAllSideMenus();

                // Make sure we can rescan, because we are admin!
                s.Driver.FindElement(By.Id("WalletRescan")).Click();
                Assert.Contains("The batch size make sure", s.Driver.PageSource);

                // We setup the fingerprint and the account key path
                s.Driver.FindElement(By.Id("WalletSettings")).Click();
                //                s.Driver.FindElement(By.Id("AccountKeys_0__MasterFingerprint")).SendKeys("8bafd160");
                //                s.Driver.FindElement(By.Id("AccountKeys_0__AccountKeyPath")).SendKeys("m/49'/0'/0'" + Keys.Enter);

                // Check the tx sent earlier arrived
                s.Driver.FindElement(By.Id("WalletTransactions")).Click();

                var walletTransactionLink = s.Driver.Url;
                Assert.Contains(tx.ToString(), s.Driver.PageSource);


                void SignWith(Mnemonic signingSource)
                {
                    // Send to bob
                    s.Driver.FindElement(By.Id("WalletSend")).Click();
                    var bob = new Key().PubKey.Hash.GetAddress(Network.RegTest);
                    SetTransactionOutput(s, 0, bob, 1);
                    s.Driver.FindElement(By.Id("SendMenu")).Click();
                    s.Driver.FindElement(By.CssSelector("button[value=seed]")).Click();

                    // Input the seed
                    s.Driver.FindElement(By.Id("SeedOrKey")).SendKeys(signingSource + Keys.Enter);

                    // Broadcast
                    Assert.Contains(bob.ToString(), s.Driver.PageSource);
                    Assert.Contains("1.00000000", s.Driver.PageSource);
                    s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
                    Assert.Equal(walletTransactionLink, s.Driver.Url);
                }

                SignWith(mnemonic);

                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();
                s.Driver.FindElement(By.Id("WalletSend")).Click();

                var jack = new Key().PubKey.Hash.GetAddress(Network.RegTest);
                SetTransactionOutput(s, 0, jack, 0.01m);
                s.Driver.FindElement(By.Id("SendMenu")).Click();

                s.Driver.FindElement(By.CssSelector("button[value=nbx-seed]")).Click();
                Assert.Contains(jack.ToString(), s.Driver.PageSource);
                Assert.Contains("0.01000000", s.Driver.PageSource);
                s.Driver.FindElement(By.CssSelector("button[value=analyze-psbt]")).Click();
                Assert.EndsWith("psbt", s.Driver.Url);
                s.Driver.FindElement(By.CssSelector("#OtherActions")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
                Assert.EndsWith("psbt/ready", s.Driver.Url);
                s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
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
                s.FindAlertMessage(StatusMessageModel.StatusSeverity.Info);
                Assert.Equal(parsedBip21.Amount.ToString(false), s.Driver.FindElement(By.Id($"Outputs_0__Amount")).GetAttribute("value"));
                Assert.Equal(parsedBip21.Address.ToString(), s.Driver.FindElement(By.Id($"Outputs_0__DestinationAddress")).GetAttribute("value"));

                s.GoToWallet(new WalletId(storeId, "BTC"), WalletsNavPages.Settings);
                var walletUrl = s.Driver.Url;

                s.Driver.FindElement(By.Id("SettingsMenu")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=view-seed]")).Click();

                // Seed backup page
                var recoveryPhrase = s.Driver.FindElements(By.Id("RecoveryPhrase")).First().GetAttribute("data-mnemonic");
                Assert.Equal(mnemonic.ToString(), recoveryPhrase);
                Assert.Contains("The recovery phrase will also be stored on the server as a hot wallet.", s.Driver.PageSource);

                // No confirmation, just a link to return to the wallet
                Assert.Empty(s.Driver.FindElements(By.Id("confirm")));
                s.Driver.FindElement(By.Id("proceed")).Click();
                Assert.Equal(walletUrl, s.Driver.Url);
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
                s.CreateNewStore();
                s.GenerateWallet("BTC", "", true, true);

                await s.Server.ExplorerNode.GenerateAsync(1);
                await s.FundStoreWallet(denomination: 50.0m);
                s.GoToWallet(navPages: WalletsNavPages.PullPayments);
                s.Driver.FindElement(By.Id("NewPullPayment")).Click();
                s.Driver.FindElement(By.Id("Name")).SendKeys("PP1");
                s.Driver.FindElement(By.Id("Amount")).Clear();
                s.Driver.FindElement(By.Id("Amount")).SendKeys("99.0");;
                s.Driver.FindElement(By.Id("Create")).Click();
                s.Driver.FindElement(By.LinkText("View")).Click();

                s.GoToWallet(navPages: WalletsNavPages.PullPayments);

                s.Driver.FindElement(By.Id("NewPullPayment")).Click();
                s.Driver.FindElement(By.Id("Name")).SendKeys("PP2");
                s.Driver.FindElement(By.Id("Amount")).Clear();
                s.Driver.FindElement(By.Id("Amount")).SendKeys("100.0");
                s.Driver.FindElement(By.Id("Create")).Click();

                // This should select the first View, ie, the last one PP2
                s.Driver.FindElement(By.LinkText("View")).Click();
                var address = await s.Server.ExplorerNode.GetNewAddressAsync();
                s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
                s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
                s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("15" + Keys.Enter);
                s.FindAlertMessage();

                // We should not be able to use an address already used
                s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
                s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
                s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("20" + Keys.Enter);
                s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error);

                address = await s.Server.ExplorerNode.GetNewAddressAsync();
                s.Driver.FindElement(By.Id("Destination")).Clear();
                s.Driver.FindElement(By.Id("Destination")).SendKeys(address.ToString());
                s.Driver.FindElement(By.Id("ClaimedAmount")).Clear();
                s.Driver.FindElement(By.Id("ClaimedAmount")).SendKeys("20" + Keys.Enter);
                s.FindAlertMessage();
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
                s.Driver.FindElement(By.Id("SendMenu")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=nbx-seed]")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();

                s.FindAlertMessage();

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

        private static void CanBrowseContent(SeleniumTester s)
        {
            s.Driver.FindElement(By.ClassName("delivery-content")).Click();
            var windows = s.Driver.WindowHandles;
            Assert.Equal(2, windows.Count);
            s.Driver.SwitchTo().Window(windows[1]);
            JObject.Parse(s.Driver.FindElement(By.TagName("body")).Text);
            s.Driver.Close();
            s.Driver.SwitchTo().Window(windows[0]);
        }

        private static void CanSetupEmailCore(SeleniumTester s)
        {
            s.Driver.FindElement(By.ClassName("dropdown-toggle")).Click();
            s.Driver.FindElement(By.ClassName("dropdown-item")).Click();
            s.Driver.FindElement(By.Id("Settings_Login")).SendKeys("test@gmail.com");
            s.Driver.FindElement(By.CssSelector("button[value=\"Save\"]")).Submit();
            s.FindAlertMessage();
            s.Driver.FindElement(By.Id("Settings_Password")).SendKeys("mypassword");
            s.Driver.FindElement(By.CssSelector("button[value=\"Save\"]")).Submit();
            Assert.Contains("Configured", s.Driver.PageSource);
            s.Driver.FindElement(By.Id("Settings_Login")).SendKeys("test_fix@gmail.com");
            s.Driver.FindElement(By.CssSelector("button[value=\"Save\"]")).Submit();
            Assert.Contains("Configured", s.Driver.PageSource);
            Assert.Contains("test_fix", s.Driver.PageSource);
            s.Driver.FindElement(By.CssSelector("button[value=\"ResetPassword\"]")).Submit();
            s.FindAlertMessage();
            Assert.DoesNotContain("Configured", s.Driver.PageSource);
            Assert.Contains("test_fix", s.Driver.PageSource);
        }

        private static string AssertUrlHasPairingCode(SeleniumTester s)
        {
            var regex = Regex.Match(new Uri(s.Driver.Url, UriKind.Absolute).Query, "pairingCode=([^&]*)");
            Assert.True(regex.Success, $"{s.Driver.Url} does not match expected regex");
            var pairingCode = regex.Groups[1].Value;
            return pairingCode;
        }

        private void SetTransactionOutput(SeleniumTester s, int index, BitcoinAddress dest, decimal amount, bool subtract = false)
        {
            s.Driver.FindElement(By.Id($"Outputs_{index}__DestinationAddress")).SendKeys(dest.ToString());
            var amountElement = s.Driver.FindElement(By.Id($"Outputs_{index}__Amount"));
            amountElement.Clear();
            amountElement.SendKeys(amount.ToString(CultureInfo.InvariantCulture));
            var checkboxElement = s.Driver.FindElement(By.Id($"Outputs_{index}__SubtractFeesFromOutput"));
            if (checkboxElement.Selected != subtract)
            {
                checkboxElement.Click();
            }
        }
    }
}
