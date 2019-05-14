using System;
using Xunit;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using BTCPayServer.Tests.Logging;
using Xunit.Abstractions;
using OpenQA.Selenium.Interactions;
using System.Linq;
using NBitcoin;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    public class ChromeTests
    {
        public ChromeTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        public void CanNavigateServerSettings()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser(true);
                s.Driver.FindElement(By.Id("ServerSettings")).Click();
                s.Driver.AssertNoError();
                s.ClickOnAllSideMenus();
                s.Driver.Quit();
            }
        }

        [Fact]
        public void NewUserLogin()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                //Register & Log Out
                var email = s.RegisterNewUser();
                s.Driver.FindElement(By.Id("Logout")).Click();
                s.Driver.AssertNoError();
                s.Driver.FindElement(By.Id("Login")).Click();
                s.Driver.AssertNoError();

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
                s.Driver.FindElement(By.Id("Login")).Click();
                s.Driver.FindElement(By.Id("Email")).SendKeys(email);
                s.Driver.FindElement(By.Id("Password")).SendKeys("abc???");
                s.Driver.FindElement(By.Id("LoginButton")).Click();
                Assert.True(s.Driver.PageSource.Contains("Stores"), "Can't Access Stores");

                s.Driver.FindElement(By.Id("MySettings")).Click();
                s.ClickOnAllSideMenus();

                s.Driver.Quit();
            }
        }

        private static void LogIn(SeleniumTester s, string email)
        {
            s.Driver.FindElement(By.Id("Login")).Click();
            s.Driver.FindElement(By.Id("Email")).SendKeys(email);
            s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
            s.Driver.FindElement(By.Id("LoginButton")).Click();
            s.Driver.AssertNoError();
        }

        [Fact]
        public void CanCreateStores()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                var alice = s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme();
                s.Driver.AssertNoError();
                Assert.Contains(store, s.Driver.PageSource);
                var storeUrl = s.Driver.Url;
                s.ClickOnAllSideMenus();

                CreateInvoice(s, store);
                s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
                var invoiceUrl = s.Driver.Url;

                // When logout we should not be able to access store and invoice details
                s.Driver.FindElement(By.Id("Logout")).Click();
                s.Driver.Navigate().GoToUrl(storeUrl);
                Assert.Contains("ReturnUrl", s.Driver.Url);
                s.Driver.Navigate().GoToUrl(invoiceUrl);
                Assert.Contains("ReturnUrl", s.Driver.Url);

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
            }
        }

        [Fact]
        public void CanCreateInvoice()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme();

                CreateInvoice(s, store);

                s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
                s.Driver.AssertNoError();
                s.Driver.Navigate().Back();
                s.Driver.FindElement(By.ClassName("invoice-checkout-link")).Click();
                Assert.NotEmpty(s.Driver.FindElements(By.Id("checkoutCtrl")));
                s.Driver.Quit();
            }
        }

        private static void CreateInvoice(SeleniumTester s, string store)
        {
            s.Driver.FindElement(By.Id("Invoices")).Click();
            s.Driver.FindElement(By.Id("CreateNewInvoice")).Click();
            s.Driver.FindElement(By.CssSelector("input#Amount.form-control")).SendKeys("100");
            s.Driver.FindElement(By.Name("StoreId")).SendKeys(store + Keys.Enter);
            s.Driver.FindElement(By.Id("Create")).Click();
            Assert.True(s.Driver.PageSource.Contains("just created!"), "Unable to create Invoice");
        }

        [Fact]
        public void CanCreateAppPoS()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                var store = s.CreateNewStore();

                s.Driver.FindElement(By.Id("Apps")).Click();
                s.Driver.FindElement(By.Id("CreateNewApp")).Click();
                s.Driver.FindElement(By.Name("Name")).SendKeys("PoS" + store);
                s.Driver.FindElement(By.CssSelector("select#SelectedAppType.form-control")).SendKeys("PointOfSale" + Keys.Enter);
                s.Driver.FindElement(By.CssSelector("select#SelectedStore.form-control")).SendKeys(store + Keys.Enter);
                s.Driver.FindElement(By.Id("Create")).Click();
                s.Driver.FindElement(By.CssSelector("input#EnableShoppingCart.form-check")).Click();
                s.Driver.FindElement(By.Id("SaveSettings")).ForceClick();
                Assert.True(s.Driver.PageSource.Contains("App updated"), "Unable to create PoS");
                s.Driver.Quit();
            }
        }

        [Fact]
        public void CanCreateAppCF()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme();

                s.Driver.FindElement(By.Id("Apps")).Click();
                s.Driver.FindElement(By.Id("CreateNewApp")).Click();
                s.Driver.FindElement(By.Name("Name")).SendKeys("CF" + store);
                s.Driver.FindElement(By.CssSelector("select#SelectedAppType.form-control")).SendKeys("Crowdfund" + Keys.Enter);
                s.Driver.FindElement(By.CssSelector("select#SelectedStore.form-control")).SendKeys(store + Keys.Enter);
                s.Driver.FindElement(By.Id("Create")).Click();
                s.Driver.FindElement(By.Id("Title")).SendKeys("Kukkstarter");
                s.Driver.FindElement(By.CssSelector("div.note-editable.card-block")).SendKeys("1BTC = 1BTC");
                s.Driver.FindElement(By.Id("TargetCurrency")).SendKeys("JPY");
                s.Driver.FindElement(By.Id("TargetAmount")).SendKeys("700");
                s.Driver.FindElement(By.Id("SaveSettings")).Submit();
                s.Driver.FindElement(By.Id("ViewApp")).ForceClick();
                s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
                Assert.True(s.Driver.PageSource.Contains("Currently Active!"), "Unable to create CF");
                s.Driver.Quit();
            } 
        }

        [Fact]
        public void CanCreatePayRequest()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                s.CreateNewStore();
                s.AddDerivationScheme();

                s.Driver.FindElement(By.Id("PaymentRequests")).Click();
                s.Driver.FindElement(By.Id("CreatePaymentRequest")).Click();
                s.Driver.FindElement(By.Id("Title")).SendKeys("Pay123");
                s.Driver.FindElement(By.Id("Amount")).SendKeys("700");
                s.Driver.FindElement(By.Id("Currency")).SendKeys("BTC");
                s.Driver.FindElement(By.Id("SaveButton")).Submit();
                s.Driver.FindElement(By.Name("ViewAppButton")).SendKeys(Keys.Return);
                s.Driver.SwitchTo().Window(s.Driver.WindowHandles.Last());
                Assert.True(s.Driver.PageSource.Contains("Amount due"), "Unable to create Payment Request");
                s.Driver.Quit();
            }
        }

        [Fact]
        public void CanManageWallet()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                s.CreateNewStore();
                s.AddDerivationScheme();

                s.Driver.FindElement(By.Id("Wallets")).Click();
                s.Driver.FindElement(By.LinkText("Manage")).Click();

                s.ClickOnAllSideMenus();

                s.Driver.Quit();
            }
        }
    }
}
