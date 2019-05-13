using System;
using Xunit;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using BTCPayServer.Tests.Logging;
using Xunit.Abstractions;
using OpenQA.Selenium.Interactions;
using System.Linq;

namespace BTCPayServer.Tests
{
    public class Base
    {
        public IWebDriver Driver { get; set; }

        public Base(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        protected void Wrap(Action<string> func)
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                func.Invoke(tester.PayTester.ServerUri.ToString());
            }
        }
    }

    public class Browsers : Base
    {
        public Browsers(ITestOutputHelper helper) : base(helper)
        {
            ChromeOptions options = new ChromeOptions();
            options.AddArguments("headless"); // Comment to view browser
            options.AddArguments("window-size=1200x600"); // Comment to view browser
            Driver = new ChromeDriver(Environment.CurrentDirectory, options);
        }

        public void RegisterNewUser(string random)
        {
            Driver.FindElement(By.Id("Register")).Click();
            Driver.FindElement(By.Id("Email")).SendKeys(random + "@a.com");
            Driver.FindElement(By.Id("Password")).SendKeys("123456");
            Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("123456");
            Driver.FindElement(By.Id("RegisterButton")).Click();
            return;
        }

        public void CreateNewStore(string random)
        {
            Driver.FindElement(By.Id("Stores")).Click();
            Driver.FindElement(By.Id("CreateStore")).Click();
            Driver.FindElement(By.Id("Name")).SendKeys("Store" + random);
            Driver.FindElement(By.Id("Create")).Click();
            return;
        }

        public void AddDerivationScheme()
        {
            Driver.FindElement(By.Id("ModifyBTC")).Click();
            Driver.FindElement(By.Id("DerivationScheme")).SendKeys("xpub661MyMwAqRbcGABgHMUXDzPzH1tU7eZaAaJQXhDXsSxsqyQzQeU6kznNfSuAyqAK9UaWSaZaMFdNiY5BCF4zBPAzSnwfUAwUhwttuAKwfRX-[legacy]");
            Driver.FindElement(By.Id("Continue")).Click();
            Driver.FindElement(By.Id("Confirm")).Click();
            Driver.FindElement(By.Id("Save")).Click();
            return;
        }

        public void CreateInvoice(string random)
        {
            Driver.FindElement(By.Id("Invoices")).Click();
            Driver.FindElement(By.Id("CreateNewInvoice")).Click();
            Driver.FindElement(By.CssSelector("input#Amount.form-control")).SendKeys("100");
            Driver.FindElement(By.Name("StoreId")).SendKeys("Deriv" + random + Keys.Enter);
            Driver.FindElement(By.Id("Create")).Click();
            return;
        }
        
    }
    
    public class ChromeTests : Browsers
    {
        private string random = (new Random()).Next(1, 1000).ToString();
        [Fact]
        public void AccessRequiresLogin()
        {
            Wrap(s =>
            {
                Driver.Navigate().GoToUrl(s);
                Assert.Contains("Login", Driver.PageSource);
                Driver.Quit();
            });
        }

        [Fact]
        public void NewUserLogin()
        {
            Wrap(s =>
            {
                //Register & Log Out
                Driver.Navigate().GoToUrl(s);
                RegisterNewUser(random);
                Driver.FindElement(By.Id("Logout")).Click();
                
                //Same User Can Log Back In
                Driver.FindElement(By.Id("Login")).Click();
                Driver.FindElement(By.Id("Email")).SendKeys(random + "@a.com");
                Driver.FindElement(By.Id("Password")).SendKeys("123456");
                Driver.FindElement(By.Id("LoginButton")).Click();

                //Change Password & Log Out
                Driver.FindElement(By.Id("MySettings")).Click();
                Driver.FindElement(By.Id("ChangePassword")).Click();
                Driver.FindElement(By.Id("OldPassword")).SendKeys("123456");
                Driver.FindElement(By.Id("NewPassword")).SendKeys("abc???");
                Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("abc???");
                Driver.FindElement(By.Id("UpdatePassword")).Click();
                Driver.FindElement(By.Id("Logout")).Click();

                //Log In With New Password
                Driver.FindElement(By.Id("Login")).Click();
                Driver.FindElement(By.Id("Email")).SendKeys(random + "@a.com");
                Driver.FindElement(By.Id("Password")).SendKeys("abc???");
                Driver.FindElement(By.Id("LoginButton")).Click();
                Assert.True(Driver.PageSource.Contains("Stores"), "Can't Access Stores");
                Driver.Quit();
            });
        }
        

        [Fact]
        public void CanCreateStores()
        {
            Wrap(s =>
            {
                Driver.Navigate().GoToUrl(s);
                RegisterNewUser(random);
                CreateNewStore(random);
                Assert.Contains("Store" + random, Driver.PageSource);
                Driver.Quit();
            });
        }

        [Fact]
        public void CanCreateInvoice()
        {
            Wrap(s =>
            {
                Driver.Navigate().GoToUrl(s);
                RegisterNewUser(random);
                CreateNewStore(random);
                AddDerivationScheme();

                Driver.FindElement(By.Id("Invoices")).Click();
                Driver.FindElement(By.Id("CreateNewInvoice")).Click();
                Driver.FindElement(By.CssSelector("input#Amount.form-control")).SendKeys("100");
                Driver.FindElement(By.Name("StoreId")).SendKeys("Deriv" + random + Keys.Enter);
                Driver.FindElement(By.Id("Create")).Click();
                Assert.True(Driver.PageSource.Contains("just created!"), "Unable to create Invoice");
                Driver.Quit();
            });
        }
        
        [Fact]
        public void CanCreateAppPoS()
        {
            Wrap(s =>
            {
                Driver.Navigate().GoToUrl(s);
                RegisterNewUser(random);
                CreateNewStore(random);

                Driver.FindElement(By.Id("Apps")).Click();
                Driver.FindElement(By.Id("CreateNewApp")).Click();
                Driver.FindElement(By.Name("Name")).SendKeys("PoS" + random);
                Driver.FindElement(By.CssSelector("select#SelectedAppType.form-control")).SendKeys("PointOfSale" + Keys.Enter);
                Driver.FindElement(By.CssSelector("select#SelectedStore.form-control")).SendKeys("Store" + random + Keys.Enter);
                Driver.FindElement(By.Id("Create")).Click();
                Driver.FindElement(By.CssSelector("input#EnableShoppingCart.form-check")).Click();
                Driver.FindElement(By.Id("SaveSettings")).Click();
                Assert.True(Driver.PageSource.Contains("App updated"), "Unable to create PoS");
                Driver.Quit();
            });
        }

        [Fact]
        public void CanCreateAppCF()
        {
            Wrap(s =>
            {
                Driver.Navigate().GoToUrl(s);
                RegisterNewUser(random);
                CreateNewStore(random);
                AddDerivationScheme();

                Driver.FindElement(By.Id("Apps")).Click();
                Driver.FindElement(By.Id("CreateNewApp")).Click();
                Driver.FindElement(By.Name("Name")).SendKeys("CF" + random);
                Driver.FindElement(By.CssSelector("select#SelectedAppType.form-control")).SendKeys("Crowdfund" + Keys.Enter);
                Driver.FindElement(By.CssSelector("select#SelectedStore.form-control")).SendKeys("Store" + random + Keys.Enter);
                Driver.FindElement(By.Id("Create")).Click();
                Driver.FindElement(By.Id("Title")).SendKeys("Kukkstarter");
                Driver.FindElement(By.CssSelector("div.note-editable.card-block")).SendKeys("1BTC = 1BTC");
                Driver.FindElement(By.Id("TargetCurrency")).SendKeys("JPY");
                Driver.FindElement(By.Id("TargetAmount")).SendKeys("700");
                Driver.FindElement(By.Id("SaveSettings")).Submit();
                Driver.FindElement(By.Id("ViewApp")).Click();
                Driver.SwitchTo().Window(Driver.WindowHandles.Last());
                Assert.True(Driver.PageSource.Contains("Currently Active!"), "Unable to create CF");
                Driver.Quit();
            }); 
        }

        [Fact]
        public void CanCreatePayRequest()
        {
            Wrap(s =>
            {
                Driver.Navigate().GoToUrl(s);
                RegisterNewUser(random);
                CreateNewStore(random);
                AddDerivationScheme();

                Driver.FindElement(By.Id("PaymentRequests")).Click();
                Driver.FindElement(By.Id("CreatePaymentRequest")).Click();
                Driver.FindElement(By.Id("Title")).SendKeys("Pay123");
                Driver.FindElement(By.Id("Amount")).SendKeys("700");
                Driver.FindElement(By.Id("Currency")).SendKeys("BTC");
                Driver.FindElement(By.Id("SaveButton")).Submit();
                Driver.FindElement(By.Name("ViewAppButton")).SendKeys(Keys.Return);
                Driver.SwitchTo().Window(Driver.WindowHandles.Last());
                Assert.True(Driver.PageSource.Contains("Amount due"), "Unable to create Payment Request");
                Driver.Quit();
            });
        }

        public ChromeTests(ITestOutputHelper helper) : base(helper)
        {
        }
    }
}
