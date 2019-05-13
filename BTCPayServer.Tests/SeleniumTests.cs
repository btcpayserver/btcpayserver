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

                //Same User Can Log Back In
                s.Driver.FindElement(By.Id("Login")).Click();
                s.Driver.FindElement(By.Id("Email")).SendKeys(email);
                s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
                s.Driver.FindElement(By.Id("LoginButton")).Click();
                s.Driver.AssertNoError();

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
                s.Driver.Quit();
            }
        }
        

        [Fact]
        public void CanCreateStores()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.Driver.AssertNoError();
                Assert.Contains(store, s.Driver.PageSource);

                s.ClickOnAllSideMenus();

                s.Driver.Quit();
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

                s.Driver.FindElement(By.Id("Invoices")).Click();
                s.Driver.FindElement(By.Id("CreateNewInvoice")).Click();
                s.Driver.FindElement(By.CssSelector("input#Amount.form-control")).SendKeys("100");
                s.Driver.FindElement(By.Name("StoreId")).SendKeys(store + Keys.Enter);
                s.Driver.FindElement(By.Id("Create")).Click();
                Assert.True(s.Driver.PageSource.Contains("just created!"), "Unable to create Invoice");
                s.Driver.Quit();
            }
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
                s.Driver.FindElement(By.Id("SaveSettings")).Click();
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
                s.Driver.FindElement(By.Id("ViewApp")).Click();
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
