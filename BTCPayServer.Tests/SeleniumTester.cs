using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using NBitcoin;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Xunit;
using System.IO;
using BTCPayServer.Tests.Logging;

namespace BTCPayServer.Tests
{
    public class SeleniumTester : IDisposable
    {
        public IWebDriver Driver { get; set; }
        public ServerTester Server { get; set; }

        public static SeleniumTester Create([CallerMemberNameAttribute] string scope = null)
        {
            var server = ServerTester.Create(scope);
            return new SeleniumTester()
            {
                Server = server
            };
        }

        public void Start()
        {
            Server.Start();
            ChromeOptions options = new ChromeOptions();
            options.AddArguments("headless"); // Comment to view browser
            options.AddArguments("window-size=1200x600"); // Comment to view browser
            if (Server.PayTester.InContainer)
            {
                Driver = new OpenQA.Selenium.Remote.RemoteWebDriver(new Uri("http://selenium:4444/wd/hub"), options);
                Logs.Tester.LogInformation("Selenium: Using remote driver");
            }
            else
            {
                Driver = new ChromeDriver(Directory.GetCurrentDirectory(), options);
                Logs.Tester.LogInformation("Selenium: Using chrome driver");
            }
            Logs.Tester.LogInformation("Selenium: Browsing to " + Server.PayTester.ServerUri);
            Driver.Navigate().GoToUrl(Server.PayTester.ServerUri);
            Driver.AssertNoError();
        }


        public string RegisterNewUser(bool isAdmin = false)
        {
            var usr = RandomUtils.GetUInt256().ToString() + "@a.com";
            Driver.FindElement(By.Id("Register")).Click();
            Driver.FindElement(By.Id("Email")).SendKeys(usr);
            Driver.FindElement(By.Id("Password")).SendKeys("123456");
            Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("123456");
            Driver.FindElement(By.Id("IsAdmin")).Click();
            Driver.FindElement(By.Id("RegisterButton")).Click();
            Driver.AssertNoError();
            return usr;
        }

        public string CreateNewStore()
        {
            var usr = "Store" + RandomUtils.GetUInt64().ToString();
            Driver.FindElement(By.Id("Stores")).Click();
            Driver.FindElement(By.Id("CreateStore")).Click();
            Driver.FindElement(By.Id("Name")).SendKeys(usr);
            Driver.FindElement(By.Id("Create")).Click();
            return usr;
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

        public void ClickOnAllSideMenus()
        {
            var links = Driver.FindElements(By.CssSelector(".nav-pills .nav-link")).Select(c => c.GetAttribute("href")).ToList();
            Assert.NotEmpty(links);
            foreach (var l in links)
            {
                Driver.Navigate().GoToUrl(l);
                Driver.AssertNoError();
            }
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


        public void Dispose()
        {
            if (Driver != null)
            {
                try
                {
                    Driver.Close();
                }
                catch { }
                Driver.Dispose();
            }
            if (Server != null)
                Server.Dispose();
        }
    }
}
