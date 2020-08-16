using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Models;
using BTCPayServer.Services;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using NBitcoin;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using Xunit;

namespace BTCPayServer.Tests
{
    public class SeleniumTester : IDisposable
    {
        public IWebDriver Driver { get; set; }
        public ServerTester Server { get; set; }

        public static SeleniumTester Create([CallerMemberNameAttribute] string scope = null, bool newDb = false)
        {
            var server = ServerTester.Create(scope, newDb);
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

        internal IWebElement AssertHappyMessage(StatusMessageModel.StatusSeverity severity = StatusMessageModel.StatusSeverity.Success)
        {
            using var cts = new CancellationTokenSource(20_000);
            while (!cts.IsCancellationRequested)
            {
                var result = Driver.FindElements(By.ClassName($"alert-{StatusMessageModel.ToString(severity)}")).Where(el => el.Displayed);
                if (result.Any())
                    return result.First();
                Thread.Sleep(100);
            }
            Logs.Tester.LogInformation(this.Driver.PageSource);
            Assert.True(false, $"Should have shown {severity} message");
            return null;
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
            Logs.Tester.LogInformation($"User: {usr} with password 123456");
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
            StoreId = Driver.FindElement(By.Id("Id")).GetAttribute("value");
            return (usr, StoreId);
        }
        public string StoreId { get; set; }

        public Mnemonic GenerateWallet(string cryptoCode = "BTC", string seed = "", bool importkeys = false, bool privkeys = false, ScriptPubKeyType format = ScriptPubKeyType.Segwit)
        {
            Driver.FindElement(By.Id($"Modify{cryptoCode}")).ForceClick();
            Driver.FindElement(By.Id("import-from-btn")).ForceClick();
            Driver.FindElement(By.Id("nbxplorergeneratewalletbtn")).ForceClick();
            Driver.WaitForElement(By.Id("ExistingMnemonic")).SendKeys(seed);
            SetCheckbox(Driver.WaitForElement(By.Id("SavePrivateKeys")), privkeys);
            SetCheckbox(Driver.WaitForElement(By.Id("ImportKeysToRPC")), importkeys);
            Driver.WaitForElement(By.Id("ScriptPubKeyType")).Click();
            Driver.WaitForElement(By.CssSelector($"#ScriptPubKeyType option[value={format}]")).Click();
            Logs.Tester.LogInformation("Trying to click btn-generate");
            Driver.WaitForElement(By.Id("btn-generate")).ForceClick();
            // Seed backup page
            AssertHappyMessage();
            if (string.IsNullOrEmpty(seed))
            {
                seed = Driver.FindElements(By.Id("recovery-phrase")).First().GetAttribute("data-mnemonic");
            }
            // Confirm seed backup
            Driver.FindElement(By.Id("confirm")).Click();
            Driver.FindElement(By.Id("submit")).Click();

            WalletId = new WalletId(StoreId, cryptoCode);
            return new Mnemonic(seed);
        }
        public WalletId WalletId { get; set; }
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
                connectionString = $"type=charge;server={Server.MerchantCharge.Client.Uri.AbsoluteUri};allowinsecure=true";
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
                Logs.Tester.LogInformation("SetCheckbox recursion, trying to click again");
                SetCheckbox(element, value);
            }
        }

        public void SetCheckbox(SeleniumTester s, string checkboxId, bool value)
        {
            SetCheckbox(s.Driver.WaitForElement(By.Id(checkboxId)), value);
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

        public void GoToProfile(ManageNavPages navPages = ManageNavPages.Index)
        {
            Driver.FindElement(By.Id("MySettings")).Click();
            if (navPages != ManageNavPages.Index)
            {
                Driver.FindElement(By.Id(navPages.ToString())).Click();
            }
        }

        public void GoToLogin()
        {
            Driver.Navigate().GoToUrl(new Uri(Server.PayTester.ServerUri, "Account/Login"));
        }

        public void GoToCreateInvoicePage()
        {
            GoToInvoices();
            Driver.FindElement(By.Id("CreateNewInvoice")).Click();
        }

        public string CreateInvoice(string storeName, decimal amount = 100, string currency = "USD", string refundEmail = "")
        {
            GoToInvoices();
            Driver.FindElement(By.Id("CreateNewInvoice")).Click();
            Driver.FindElement(By.Id("Amount")).SendKeys(amount.ToString(CultureInfo.InvariantCulture));
            var currencyEl = Driver.FindElement(By.Id("Currency"));
            currencyEl.Clear();
            currencyEl.SendKeys(currency);
            Driver.FindElement(By.Id("BuyerEmail")).SendKeys(refundEmail);
            Driver.FindElement(By.Name("StoreId")).SendKeys(storeName + Keys.Enter);
            Driver.FindElement(By.Id("Create")).ForceClick();
            Assert.True(Driver.PageSource.Contains("just created!"), "Unable to create Invoice");
            var statusElement = Driver.FindElement(By.ClassName("alert-success"));
            var id = statusElement.Text.Split(" ")[1];

            return id;
        }

        public async Task FundStoreWallet(WalletId walletId = null, int coins = 1, decimal denomination = 1m)
        {
            walletId ??= WalletId;
            GoToWallet(walletId, WalletsNavPages.Receive);
            Driver.FindElement(By.Id("generateButton")).Click();
            var addressStr = Driver.FindElement(By.Id("vue-address")).GetProperty("value");
            var address = BitcoinAddress.Create(addressStr, ((BTCPayNetwork)Server.NetworkProvider.GetNetwork(walletId.CryptoCode)).NBitcoinNetwork);
            for (int i = 0; i < coins; i++)
            {
                await Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(denomination));
            }
        }

        public void PayInvoice(WalletId walletId, string invoiceId)
        {
            GoToInvoiceCheckout(invoiceId);
            var bip21 = Driver.FindElement(By.ClassName("payment__details__instruction__open-wallet__btn"))
                .GetAttribute("href");
            Assert.Contains($"{PayjoinClient.BIP21EndpointKey}", bip21);

            GoToWallet(walletId, WalletsNavPages.Send);
            Driver.FindElement(By.Id("bip21parse")).Click();
            Driver.SwitchTo().Alert().SendKeys(bip21);
            Driver.SwitchTo().Alert().Accept();
            Driver.ScrollTo(By.Id("SendMenu"));
            Driver.FindElement(By.Id("SendMenu")).ForceClick();
            Driver.FindElement(By.CssSelector("button[value=nbx-seed]")).Click();
            Driver.FindElement(By.CssSelector("button[value=broadcast]")).ForceClick();
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

        public void GoToWallet(WalletId walletId = null, WalletsNavPages navPages = WalletsNavPages.Send)
        {
            walletId ??= WalletId;
            Driver.Navigate().GoToUrl(new Uri(Server.PayTester.ServerUri, $"wallets/{walletId}"));
            if (navPages != WalletsNavPages.Transactions)
            {
                Driver.FindElement(By.Id($"Wallet{navPages}")).Click();
            }
        }

        public void GoToInvoice(string id)
        {
            GoToInvoices();
            foreach (var el in Driver.FindElements(By.ClassName("invoice-details-link")))
            {
                if (el.GetAttribute("href").Contains(id, StringComparison.OrdinalIgnoreCase))
                {
                    el.Click();
                    break;
                }
            }
        }
    }
}
