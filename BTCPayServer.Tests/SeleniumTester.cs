using System;
using BTCPayServer;
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
using System.Threading;

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
            options.AddArgument("shm-size=2g");
            if (Server.PayTester.InContainer)
            {
                options.AddArgument("no-sandbox");
            }
            Driver = new ChromeDriver(Server.PayTester.InContainer ? "/usr/bin" : Directory.GetCurrentDirectory(), options);
            Logs.Tester.LogInformation("Selenium: Using chrome driver");
            Logs.Tester.LogInformation("Selenium: Browsing to " + Server.PayTester.ServerUri);
            Logs.Tester.LogInformation($"Selenium: Resolution {Driver.Manage().Window.Size}");
            Driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
            Driver.Navigate().GoToUrl(Server.PayTester.ServerUri);
            Driver.AssertNoError();
        }

        public string Link(string relativeLink)
        {
            return Server.PayTester.ServerUri.AbsoluteUri.WithoutEndingSlash() + relativeLink.WithStartingSlash();
        }

        public string RegisterNewUser(bool isAdmin = false)
        {
            var usr = RandomUtils.GetUInt256().ToString() + "@a.com";
            Driver.FindElement(By.Id("Register")).Click();
            Driver.FindElement(By.Id("Email")).SendKeys(usr);
            Driver.FindElement(By.Id("Password")).SendKeys("123456");
            Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("123456");
            if (isAdmin)
                Driver.FindElement(By.Id("IsAdmin")).Click();
            Driver.FindElement(By.Id("RegisterButton")).Click();
            Driver.AssertNoError();
            return usr;
        }

        public (string storeName, string storeId) CreateNewStore()
        {
            var usr = "Store" + RandomUtils.GetUInt64().ToString();
            Driver.FindElement(By.Id("Stores")).Click();
            Driver.FindElement(By.Id("CreateStore")).Click();
            Driver.FindElement(By.Id("Name")).SendKeys(usr);
            Driver.FindElement(By.Id("Create")).Click();
            
            return (usr, Driver.FindElement(By.Id("Id")).GetAttribute("value"));
        }

        public void AddDerivationScheme(string derivationScheme = "xpub661MyMwAqRbcGABgHMUXDzPzH1tU7eZaAaJQXhDXsSxsqyQzQeU6kznNfSuAyqAK9UaWSaZaMFdNiY5BCF4zBPAzSnwfUAwUhwttuAKwfRX-[legacy]")
        {
            Driver.FindElement(By.Id("ModifyBTC")).ForceClick();
            Driver.FindElement(By.ClassName("store-derivation-scheme")).SendKeys(derivationScheme);
            Driver.FindElement(By.Id("Continue")).ForceClick();
            Driver.FindElement(By.Id("Confirm")).ForceClick();
            Driver.FindElement(By.Id("Save")).ForceClick();
            return;
        }

        public void ClickOnAllSideMenus()
        {
            var links = Driver.FindElements(By.CssSelector(".nav-pills .nav-link")).Select(c => c.GetAttribute("href")).ToList();
            Driver.AssertNoError();
            Assert.NotEmpty(links);
            foreach (var l in links)
            {
                Driver.Navigate().GoToUrl(l);
                Driver.AssertNoError();
            }
        }

        public string CreateInvoice(string random, string refundEmail = "")
        {
            Driver.FindElement(By.Id("Invoices")).Click();
            Driver.FindElement(By.Id("CreateNewInvoice")).Click();
            Driver.FindElement(By.CssSelector("input#Amount.form-control")).SendKeys("100");
            Driver.FindElement(By.Name("StoreId")).SendKeys("Deriv" + random + Keys.Enter);
            Driver.FindElement(By.Id("Create")).Click();
            var statusElement = Driver.FindElement(By.ClassName("alert-success"));
            var id = statusElement.Text.Split(" ")[1];
            if (!string.IsNullOrEmpty(refundEmail))
            {
                GoToInvoiceCheckout(id);
                Driver.FindElement(By.Id("emailAddressFormInput")).SendKeys(refundEmail);
                Driver.FindElement(By.Id("emailAddressForm")).FindElement(By.CssSelector("button.action-button"))
                    .Click();
            }

            return id;
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

        internal void AssertNotFound()
        {
            Assert.Contains("Status Code: 404; Not Found", Driver.PageSource);
        }

        public void GoToHome()
        {
            Driver.Navigate().GoToUrl(Server.PayTester.ServerUri);
        }

        public void Logout()
        {
            Driver.FindElement(By.Id("Logout")).Click();
        }

        public void Login(string user, string password)
        {
            Driver.FindElement(By.Id("Email")).SendKeys(user);
            Driver.FindElement(By.Id("Password")).SendKeys(password);
            Driver.FindElement(By.Id("LoginButton")).Click();

        }

        public void GoToStore(string storeId)
        {
            Driver.FindElement(By.Id("Stores")).Click();
            Driver.FindElement(By.Id($"update-store-{storeId}")).Click();
        }
        
        public void GoToInvoiceCheckout(string invoiceId)
        {
            Driver.FindElement(By.Id("Invoices")).Click();
            Driver.FindElement(By.Id($"invoice-checkout-{invoiceId}")).Click();
        }
        
        
        public void SetCheckbox(IWebElement element, bool value)
        {
            if ((value && !element.Selected) || (!value && element.Selected))
            {
                element.Click();
            }
        }

        public void SetCheckbox(SeleniumTester s, string inputName, bool value)
        {
            SetCheckbox(s.Driver.FindElement(By.Name(inputName)), value);
        }
    }
}
