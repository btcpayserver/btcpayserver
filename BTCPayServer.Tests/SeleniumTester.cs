using System;
using BTCPayServer;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using NBitcoin;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Xunit;
using System.IO;
using System.Net.Http;
using System.Reflection;
using BTCPayServer.Tests.Logging;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Views.Stores;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium.Interactions;

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


        public async Task StartAsync()
        {
            await Server.StartAsync();
            ChromeOptions options = new ChromeOptions();
            var isDebug = !Server.PayTester.InContainer;

            if (!isDebug)
            {
                options.AddArguments("headless"); // Comment to view browser
                options.AddArguments("window-size=1200x1000"); // Comment to view browser
            }
            options.AddArgument("shm-size=2g");
            if (Server.PayTester.InContainer)
            {
                options.AddArgument("no-sandbox");
            }
            Driver = new ChromeDriver(Server.PayTester.InContainer ? "/usr/bin" : Directory.GetCurrentDirectory(), options);
            if (isDebug)
            {
                //when running locally, depending on your resolution, the website may go into mobile responsive mode and screw with navigation of tests
                Driver.Manage().Window.Maximize();
            }
            Logs.Tester.LogInformation("Selenium: Using chrome driver");
            Logs.Tester.LogInformation("Selenium: Browsing to " + Server.PayTester.ServerUri);
            Logs.Tester.LogInformation($"Selenium: Resolution {Driver.Manage().Window.Size}");
            Driver.Manage().Timeouts().ImplicitWait = ImplicitWait;
            GoToRegister();
            Driver.AssertNoError();
        }

        internal void AssertHappyMessage()
        {
            using var cts = new CancellationTokenSource(20_000);
            while (!cts.IsCancellationRequested)
            {
                var success = Driver.FindElements(By.ClassName("alert-success")).Where(el => el.Displayed).Any();
                if (success)
                    return;
                Thread.Sleep(100);
            }
            Logs.Tester.LogInformation(this.Driver.PageSource);
            Assert.True(false, "Should have shown happy message");
        }

        public static readonly TimeSpan ImplicitWait = TimeSpan.FromSeconds(10);
        public string Link(string relativeLink)
        {
            return Server.PayTester.ServerUri.AbsoluteUri.WithoutEndingSlash() + relativeLink.WithStartingSlash();
        }

        public void GoToRegister()
        {
            Driver.Navigate().GoToUrl(this.Link("/Account/Register"));
        }
        public string RegisterNewUser(bool isAdmin = false)
        {
            var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
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

        public string GenerateWallet(string cryptoCode = "BTC", string seed = "", bool importkeys = false, bool privkeys = false)
        {
            Driver.FindElement(By.Id($"Modify{cryptoCode}")).ForceClick();
            Driver.FindElement(By.Id("import-from-btn")).ForceClick();
            Driver.FindElement(By.Id("nbxplorergeneratewalletbtn")).ForceClick();
            Driver.FindElement(By.Id("ExistingMnemonic")).SendKeys(seed);
            SetCheckbox(Driver.FindElement(By.Id("SavePrivateKeys")), privkeys);
            SetCheckbox(Driver.FindElement(By.Id("ImportKeysToRPC")), importkeys);
            Driver.FindElement(By.Id("btn-generate")).ForceClick();
            AssertHappyMessage();
            if (string.IsNullOrEmpty(seed))
            {
                seed = Driver.FindElements(By.ClassName("alert-success")).First().FindElement(By.TagName("code")).Text;
            }
            Driver.FindElement(By.Id("Confirm")).ForceClick();
            AssertHappyMessage();
            return seed;
        }

        public void AddDerivationScheme(string cryptoCode = "BTC", string derivationScheme = "xpub661MyMwAqRbcGABgHMUXDzPzH1tU7eZaAaJQXhDXsSxsqyQzQeU6kznNfSuAyqAK9UaWSaZaMFdNiY5BCF4zBPAzSnwfUAwUhwttuAKwfRX-[legacy]")
        {
            Driver.FindElement(By.Id($"Modify{cryptoCode}")).ForceClick();
            Driver.FindElement(By.ClassName("store-derivation-scheme")).SendKeys(derivationScheme);
            Driver.FindElement(By.Id("Continue")).ForceClick();
            Driver.FindElement(By.Id("Confirm")).ForceClick();
            AssertHappyMessage();
        }

        public void AddLightningNode(string cryptoCode, LightningConnectionType connectionType)
        {
            string connectionString = null;
            if (connectionType == LightningConnectionType.Charge)
                connectionString = "type=charge;server=" + Server.MerchantCharge.Client.Uri.AbsoluteUri;
            else if (connectionType == LightningConnectionType.CLightning)
                connectionString = "type=clightning;server=" + ((CLightningClient)Server.MerchantLightningD).Address.AbsoluteUri;
            else if (connectionType == LightningConnectionType.LndREST)
                connectionString = $"type=lnd-rest;server={Server.MerchantLnd.Swagger.BaseUrl};allowinsecure=true";
            else
                throw new NotSupportedException(connectionType.ToString());

            Driver.FindElement(By.Id($"Modify-Lightning{cryptoCode}")).ForceClick();
            Driver.FindElement(By.Name($"ConnectionString")).SendKeys(connectionString);
            Driver.FindElement(By.Id($"save")).ForceClick();
        }

        public void AddInternalLightningNode(string cryptoCode)
        {
            Driver.FindElement(By.Id($"Modify-Lightning{cryptoCode}")).ForceClick();
            Driver.FindElement(By.Id($"internal-ln-node-setter")).ForceClick();
            Driver.FindElement(By.Id($"save")).ForceClick();
        }

        public void ClickOnAllSideMenus()
        {
            var links = Driver.FindElements(By.CssSelector(".nav-pills .nav-link")).Select(c => c.GetAttribute("href")).ToList();
            Driver.AssertNoError();
            Assert.NotEmpty(links);
            foreach (var l in links)
            {
                Logs.Tester.LogInformation($"Checking no error on {l}");
                Driver.Navigate().GoToUrl(l);
                Driver.AssertNoError();
            }
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
            Assert.Contains("404 - Page not found</h1>", Driver.PageSource);
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

        public void GoToStore(string storeId, StoreNavPages storeNavPage = StoreNavPages.Index)
        {
            Driver.FindElement(By.Id("Stores")).Click();
            Driver.FindElement(By.Id($"update-store-{storeId}")).Click();
            if (storeNavPage != StoreNavPages.Index)
            {
                Driver.FindElement(By.Id(storeNavPage.ToString())).Click();
            }
        }

        public void GoToInvoiceCheckout(string invoiceId)
        {
            Driver.FindElement(By.Id("Invoices")).Click();
            Driver.FindElement(By.Id($"invoice-checkout-{invoiceId}")).Click();
            CheckForJSErrors();
        }


        public void SetCheckbox(IWebElement element, bool value)
        {
            if ((value && !element.Selected) || (!value && element.Selected))
            {
                element.Click();
            }

            if (value != element.Selected)
            {
                SetCheckbox(element, value);
            }
        }

        public void SetCheckbox(SeleniumTester s, string inputName, bool value)
        {
            SetCheckbox(s.Driver.FindElement(By.Name(inputName)), value);
        }

        public void ScrollToElement(IWebElement element)
        {
            Actions actions = new Actions(Driver);
            actions.MoveToElement(element);
            actions.Perform();
        }

        public void GoToInvoices()
        {
            Driver.FindElement(By.Id("Invoices")).Click();
        }

        public void GoToCreateInvoicePage()
        {
            GoToInvoices();
            Driver.FindElement(By.Id("CreateNewInvoice")).Click();
        }

        public string CreateInvoice(string store, decimal amount = 100, string currency = "USD", string refundEmail = "")
        {
            GoToInvoices();
            Driver.FindElement(By.Id("CreateNewInvoice")).Click();
            Driver.FindElement(By.Id("Amount")).SendKeys(amount.ToString(CultureInfo.InvariantCulture));
            var currencyEl = Driver.FindElement(By.Id("Currency"));
            currencyEl.Clear();
            currencyEl.SendKeys(currency);
            Driver.FindElement(By.Id("BuyerEmail")).SendKeys(refundEmail);
            Driver.FindElement(By.Name("StoreId")).SendKeys(store + Keys.Enter);
            Driver.FindElement(By.Id("Create")).ForceClick();
            Assert.True(Driver.PageSource.Contains("just created!"), "Unable to create Invoice");
            var statusElement = Driver.FindElement(By.ClassName("alert-success"));
            var id = statusElement.Text.Split(" ")[1];

            return id;
        }



        private void CheckForJSErrors()
        {
            //wait for seleniun update: https://stackoverflow.com/questions/57520296/selenium-webdriver-3-141-0-driver-manage-logs-availablelogtypes-throwing-syste
            //            var errorStrings = new List<string> 
            //            { 
            //                "SyntaxError", 
            //                "EvalError", 
            //                "ReferenceError", 
            //                "RangeError", 
            //                "TypeError", 
            //                "URIError" 
            //            };
            //
            //            var jsErrors = Driver.Manage().Logs.GetLog(LogType.Browser).Where(x => errorStrings.Any(e => x.Message.Contains(e)));
            //
            //            if (jsErrors.Any())
            //            {
            //                Logs.Tester.LogInformation("JavaScript error(s):" + Environment.NewLine + jsErrors.Aggregate("", (s, entry) => s + entry.Message + Environment.NewLine));
            //            }
            //            Assert.Empty(jsErrors);

        }


    }
}
