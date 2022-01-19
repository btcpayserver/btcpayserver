using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Xunit;

namespace BTCPayServer.Tests
{
    public class SeleniumTester : IDisposable
    {
        public IWebDriver Driver { get; set; }
        public ServerTester Server { get; set; }
        public WalletId WalletId { get; set; }

        public string StoreId { get; set; }

        public static readonly TimeSpan ImplicitWait = TimeSpan.FromSeconds(5);

        public async Task StartAsync()
        {
            Server.PayTester.NoCSP = true;
            await Server.StartAsync();

            var windowSize = (Width: 1200, Height: 1000);
            var builder = new ConfigurationBuilder();
            builder.AddUserSecrets("AB0AC1DD-9D26-485B-9416-56A33F268117");
            var config = builder.Build();

            // Run `dotnet user-secrets set RunSeleniumInBrowser true` to run tests in browser
            var runInBrowser = config["RunSeleniumInBrowser"] == "true";
            // Reset this using `dotnet user-secrets remove RunSeleniumInBrowser`

            var chromeDriverPath = config["ChromeDriverDirectory"] ?? (Server.PayTester.InContainer ? "/usr/bin" : Directory.GetCurrentDirectory());

            var options = new ChromeOptions();
            if (!runInBrowser)
            {
                options.AddArguments("headless");
            }
            options.AddArguments($"window-size={windowSize.Width}x{windowSize.Height}");
            options.AddArgument("shm-size=2g");
            options.AddArgument("start-maximized");
            if (Server.PayTester.InContainer)
            {
                Driver = new OpenQA.Selenium.Remote.RemoteWebDriver(new Uri("http://selenium:4444/wd/hub"), new RemoteSessionSettings(options));
                var containerIp = File.ReadAllText("/etc/hosts").Split('\n', StringSplitOptions.RemoveEmptyEntries).Last()
                    .Split('\t', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                TestLogs.LogInformation($"Selenium: Container's IP {containerIp}");
                ServerUri = new Uri(Server.PayTester.ServerUri.AbsoluteUri.Replace($"http://{Server.PayTester.HostName}", $"http://{containerIp}", StringComparison.OrdinalIgnoreCase), UriKind.Absolute);
            }
            else
            {
                var cds = ChromeDriverService.CreateDefaultService(chromeDriverPath);
                cds.EnableVerboseLogging = true;
                cds.Port = Utils.FreeTcpPort();
                cds.HostName = "127.0.0.1";
                cds.Start();
                Driver = new ChromeDriver(cds, options,
                    // A bit less than test timeout
                    TimeSpan.FromSeconds(50));
                ServerUri = Server.PayTester.ServerUri;
            }
            Driver.Manage().Window.Maximize();

            TestLogs.LogInformation($"Selenium: Using {Driver.GetType()}");
            TestLogs.LogInformation($"Selenium: Browsing to {ServerUri}");
            TestLogs.LogInformation($"Selenium: Resolution {Driver.Manage().Window.Size}");
            GoToRegister();
            Driver.AssertNoError();
        }

        /// <summary>
        /// Use this ServerUri when trying to browse with selenium
        /// Because for some reason, the selenium container can't resolve the tests container domain name
        /// </summary>
        public Uri ServerUri;
        internal IWebElement FindAlertMessage(StatusMessageModel.StatusSeverity severity = StatusMessageModel.StatusSeverity.Success)
        {
            return FindAlertMessage(new[] { severity });
        }
        internal IWebElement FindAlertMessage(params StatusMessageModel.StatusSeverity[] severity)
        {
            var className = string.Join(", ", severity.Select(statusSeverity => $".alert-{StatusMessageModel.ToString(statusSeverity)}"));
            IWebElement el;
            try
            {
                var elements = Driver.FindElements(By.CssSelector(className));
                el = elements.FirstOrDefault(e => e.Displayed);
                if (el is null)
                    el = elements.FirstOrDefault();
                if (el is null)
                    el = Driver.WaitForElement(By.CssSelector(className));
            }
            catch (NoSuchElementException)
            {
                el = Driver.WaitForElement(By.CssSelector(className));
            }
            if (el is null)
                throw new NoSuchElementException($"Unable to find {className}");
            if (!el.Displayed)
                throw new ElementNotVisibleException($"{className} is present, but not displayed: {el.GetAttribute("id")} - Text: {el.Text}");
            return el;
        }

        public string Link(string relativeLink)
        {
            return ServerUri.AbsoluteUri.WithoutEndingSlash() + relativeLink.WithStartingSlash();
        }

        public void GoToRegister()
        {
            Driver.Navigate().GoToUrl(Link("/register"));
        }

        public string RegisterNewUser(bool isAdmin = false)
        {
            var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
            TestLogs.LogInformation($"User: {usr} with password 123456");
            Driver.FindElement(By.Id("Email")).SendKeys(usr);
            Driver.FindElement(By.Id("Password")).SendKeys("123456");
            Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("123456");
            if (isAdmin)
                Driver.FindElement(By.Id("IsAdmin")).Click();
            Driver.FindElement(By.Id("RegisterButton")).Click();
            Driver.AssertNoError();
            return usr;
        }

        public (string storeName, string storeId) CreateNewStore(bool keepId = true)
        {
            // If there's no store yet, there is no dropdown toggle
            if (Driver.PageSource.Contains("id=\"StoreSelectorToggle\""))
            {
                Driver.FindElement(By.Id("StoreSelectorToggle")).Click();
            }
            Driver.WaitForElement(By.Id("StoreSelectorCreate")).Click();
            var name = "Store" + RandomUtils.GetUInt64();
            Driver.WaitForElement(By.Id("Name")).SendKeys(name);
            Driver.WaitForElement(By.Id("Create")).Click();
            Driver.FindElement(By.Id("StoreNav-StoreSettings")).Click();
            Driver.FindElement(By.Id($"SectionNav-{StoreNavPages.General.ToString()}")).Click();
            var storeId = Driver.WaitForElement(By.Id("Id")).GetAttribute("value");
            Driver.FindElement(By.Id($"SectionNav-{StoreNavPages.Payment.ToString()}")).Click();
            if (keepId)
                StoreId = storeId;
            return (name, storeId);
        }

        public Mnemonic GenerateWallet(string cryptoCode = "BTC", string seed = "", bool importkeys = false, bool privkeys = false, ScriptPubKeyType format = ScriptPubKeyType.Segwit)
        {
            var isImport = !string.IsNullOrEmpty(seed);
            GoToWalletSettings(cryptoCode);

            // Replace previous wallet case
            if (Driver.PageSource.Contains("id=\"ChangeWalletLink\""))
            {
                Driver.FindElement(By.Id("ActionsDropdownToggle")).Click();
                Driver.WaitForElement(By.Id("ChangeWalletLink")).Click();
                Driver.WaitForElement(By.Id("ConfirmInput")).SendKeys("REPLACE");
                Driver.FindElement(By.Id("ConfirmContinue")).Click();
            }

            if (isImport)
            {
                TestLogs.LogInformation("Progressing with existing seed");
                Driver.FindElement(By.Id("ImportWalletOptionsLink")).Click();
                Driver.FindElement(By.Id("ImportSeedLink")).Click();
                Driver.FindElement(By.Id("ExistingMnemonic")).SendKeys(seed);
                Driver.SetCheckbox(By.Id("SavePrivateKeys"), privkeys);
            }
            else
            {
                var option = privkeys ? "Hotwallet" : "Watchonly";
                TestLogs.LogInformation($"Generating new seed ({option})");
                Driver.FindElement(By.Id("GenerateWalletLink")).Click();
                Driver.FindElement(By.Id($"Generate{option}Link")).Click();
            }

            Driver.FindElement(By.Id("ScriptPubKeyType")).Click();
            Driver.FindElement(By.CssSelector($"#ScriptPubKeyType option[value={format}]")).Click();

            Driver.ToggleCollapse("AdvancedSettings");
            Driver.SetCheckbox(By.Id("ImportKeysToRPC"), importkeys);
            Driver.FindElement(By.Id("Continue")).Click();

            if (isImport)
            {
                // Confirm addresses
                Driver.FindElement(By.Id("Confirm")).Click();
            }
            else
            {
                // Seed backup
                FindAlertMessage();
                if (string.IsNullOrEmpty(seed))
                {
                    seed = Driver.FindElements(By.Id("RecoveryPhrase")).First().GetAttribute("data-mnemonic");
                }

                // Confirm seed backup
                Driver.FindElement(By.Id("confirm")).Click();
                Driver.FindElement(By.Id("submit")).Click();
            }

            WalletId = new WalletId(StoreId, cryptoCode);
            return new Mnemonic(seed);
        }

        /// <summary>
        /// Assume to be in store's settings
        /// </summary>
        /// <param name="cryptoCode"></param>
        /// <param name="derivationScheme"></param>
        public void AddDerivationScheme(string cryptoCode = "BTC", string derivationScheme = "xpub661MyMwAqRbcGABgHMUXDzPzH1tU7eZaAaJQXhDXsSxsqyQzQeU6kznNfSuAyqAK9UaWSaZaMFdNiY5BCF4zBPAzSnwfUAwUhwttuAKwfRX-[legacy]")
        {
            GoToWalletSettings(cryptoCode);

            Driver.FindElement(By.Id("ImportWalletOptionsLink")).Click();
            Driver.FindElement(By.Id("ImportXpubLink")).Click();
            Driver.FindElement(By.Id("DerivationScheme")).SendKeys(derivationScheme);
            Driver.FindElement(By.Id("Continue")).Click();
            Driver.FindElement(By.Id("Confirm")).Click();
            FindAlertMessage();
        }

        public void AddLightningNode()
        {
            AddLightningNode(null, null, true);
        }
        
        public void AddLightningNode(LightningConnectionType? connectionType = null, bool test = true)
        {
            AddLightningNode(null, connectionType, test);
        }
        
        public void AddLightningNode(string cryptoCode = null, LightningConnectionType? connectionType = null, bool test = true)
        {
            cryptoCode ??= "BTC";
            Driver.FindElement(By.Id($"StoreNav-Lightning{cryptoCode}")).Click();
            if (Driver.PageSource.Contains("id=\"SetupLightningNodeLink\""))
            {
                Driver.FindElement(By.Id("SetupLightningNodeLink")).Click();
            }

            var connectionString = connectionType switch
            {
                LightningConnectionType.Charge =>
                    $"type=charge;server={Server.MerchantCharge.Client.Uri.AbsoluteUri};allowinsecure=true",
                LightningConnectionType.CLightning =>
                    $"type=clightning;server={((CLightningClient)Server.MerchantLightningD).Address.AbsoluteUri}",
                LightningConnectionType.LndREST =>
                    $"type=lnd-rest;server={Server.MerchantLnd.Swagger.BaseUrl};allowinsecure=true",
                _ => null
            };

            if (connectionString == null)
            {
                Assert.True(Driver.FindElement(By.Id("LightningNodeType-Internal")).Enabled, "Usage of the internal Lightning node is disabled.");
                Driver.FindElement(By.CssSelector("label[for=\"LightningNodeType-Internal\"]")).Click();
            }
            else
            {
                Driver.FindElement(By.CssSelector("label[for=\"LightningNodeType-Custom\"]")).Click();
                Driver.WaitForElement(By.Id("ConnectionString")).Clear();
                Driver.FindElement(By.Id("ConnectionString")).SendKeys(connectionString);
                if (test)
                {
                    Driver.FindElement(By.Id("test")).Click();
                    Assert.Contains("Connection to the Lightning node successful.", FindAlertMessage().Text);
                }
            }

            Driver.FindElement(By.Id("save")).Click();
            Assert.Contains($"{cryptoCode} Lightning node updated.", FindAlertMessage().Text);

            var enabled = Driver.FindElement(By.Id($"{cryptoCode}LightningEnabled"));
            if (enabled.Text == "Enable")
            {
                enabled.Click();
                Assert.Contains($"{cryptoCode} Lightning payments are now enabled for this store.", FindAlertMessage().Text);
            }
        }

        public Logging.ILog TestLogs => Server.TestLogs;
        public void ClickOnAllSectionLinks()
        {
            var links = Driver.FindElements(By.CssSelector("#SectionNav .nav-link")).Select(c => c.GetAttribute("href")).ToList();
            Driver.AssertNoError();
            foreach (var l in links)
            {
                TestLogs.LogInformation($"Checking no error on {l}");
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
                    Driver.Quit();
                }
                catch
                {
                    // ignored
                }

                Driver.Dispose();
            }

            Server?.Dispose();
        }

        internal void AssertNotFound()
        {
            Assert.Contains("404 - Page not found</h1>", Driver.PageSource);
        }

        internal void AssertAccessDenied()
        {
            Assert.Contains("- Denied</h", Driver.PageSource);
        }

        public void GoToHome()
        {
            Driver.Navigate().GoToUrl(ServerUri);
        }

        public void Logout()
        {
            Driver.FindElement(By.Id("Nav-Account")).Click();
            Driver.FindElement(By.Id("Nav-Logout")).Click();
        }

        public void Login(string user, string password)
        {
            Driver.FindElement(By.Id("Email")).SendKeys(user);
            Driver.FindElement(By.Id("Password")).SendKeys(password);
            Driver.FindElement(By.Id("LoginButton")).Click();
        }

        public void GoToStore(StoreNavPages storeNavPage = StoreNavPages.General)
        {
            GoToStore(null, storeNavPage);
        }
        
        public void GoToStore(string storeId, StoreNavPages storeNavPage = StoreNavPages.General)
        {
            if (storeId is not null)
                GoToUrl($"/stores/{storeId}/");
                
            Driver.FindElement(By.Id("StoreNav-StoreSettings")).Click();

            if (storeNavPage != StoreNavPages.General)
            {
                switch (storeNavPage)
                {
                    case StoreNavPages.Dashboard:
                    case StoreNavPages.Payouts:
                    case StoreNavPages.PayButton:
                    case StoreNavPages.PullPayments:
                        Driver.FindElement(By.Id($"StoreNav-{storeNavPage.ToString()}")).Click();
                        break;
                    default:
                        Driver.FindElement(By.Id($"SectionNav-{storeNavPage.ToString()}")).Click();
                        break;
                }
            }
        }
        
        public void GoToWalletSettings(string cryptoCode = "BTC")
        {
            Driver.FindElement(By.Id($"StoreNav-Wallet{cryptoCode}")).Click();
            if (Driver.PageSource.Contains("id=\"SectionNav-Settings\""))
            {
                Driver.FindElement(By.Id("SectionNav-Settings")).Click();
            }
        }

        public void GoToLightningSettings(string cryptoCode = "BTC")
        {
            GoToStore(StoreNavPages.Payment);
            Driver.FindElement(By.Id($"StoreNav-Lightning{cryptoCode}")).Click();
            // if Lightning is already set up we need to navigate to the settings
            if (Driver.PageSource.Contains("id=\"SectionNav-LightningSettings\""))
            {
                Driver.FindElement(By.Id("SectionNav-LightningSettings")).Click();
            }
        }

        public void SelectStoreContext(string storeId)
        {
            Driver.FindElement(By.Id("StoreSelectorToggle")).Click();
            Driver.FindElement(By.Id($"StoreSelectorMenuItem-{storeId}")).Click();
        }

        public void GoToInvoiceCheckout(string invoiceId)
        {
            Driver.FindElement(By.Id("StoreNav-Invoices")).Click();
            Driver.FindElement(By.Id($"invoice-checkout-{invoiceId}")).Click();
            CheckForJSErrors();
        }

        public void GoToInvoice(string id)
        {
            GoToUrl($"/invoices/{id}/");
        }

        public void GoToInvoices(string storeId = null)
        {
            if (storeId is null)
            {
                Driver.FindElement(By.Id("StoreNav-Invoices")).Click();
            }
            else
            {
                GoToUrl(storeId == null ? "/invoices/" : $"/stores/{storeId}/invoices/");
            }
        }

        public void GoToProfile(ManageNavPages navPages = ManageNavPages.Index)
        {
            Driver.FindElement(By.Id("Nav-Account")).Click();
            Driver.FindElement(By.Id("Nav-ManageAccount")).Click();
            if (navPages != ManageNavPages.Index)
            {
                Driver.FindElement(By.Id($"SectionNav-{navPages.ToString()}")).Click();
            }
        }

        public void GoToLogin()
        {
            GoToUrl("/login");
        }

        public string CreateInvoice(decimal? amount = 100,
            string currency = "USD",
            string refundEmail = "",
            string defaultPaymentMethod = null,
            bool? requiresRefundEmail = null,
            StatusMessageModel.StatusSeverity expectedSeverity = StatusMessageModel.StatusSeverity.Success
        )
        {
            return CreateInvoice(null, amount, currency, refundEmail, defaultPaymentMethod, requiresRefundEmail, expectedSeverity);
        }

        public string CreateInvoice(
            string storeId,
            decimal? amount = 100,
            string currency = "USD",
            string refundEmail = "",
            string defaultPaymentMethod = null,
            bool? requiresRefundEmail = null,
            StatusMessageModel.StatusSeverity expectedSeverity = StatusMessageModel.StatusSeverity.Success
        )
        {
            GoToInvoices(storeId);
            Driver.FindElement(By.Id("CreateNewInvoice")).Click();
            if (amount is decimal v)
                Driver.FindElement(By.Id("Amount")).SendKeys(v.ToString(CultureInfo.InvariantCulture));
            var currencyEl = Driver.FindElement(By.Id("Currency"));
            currencyEl.Clear();
            currencyEl.SendKeys(currency);
            Driver.FindElement(By.Id("BuyerEmail")).SendKeys(refundEmail);
            if (defaultPaymentMethod is not null)
                new SelectElement(Driver.FindElement(By.Name("DefaultPaymentMethod"))).SelectByValue(defaultPaymentMethod);
            if (requiresRefundEmail is bool)
                new SelectElement(Driver.FindElement(By.Name("RequiresRefundEmail"))).SelectByValue(requiresRefundEmail == true ? "1" : "2");
            Driver.FindElement(By.Id("Create")).Click();

            var statusElement = FindAlertMessage(expectedSeverity);
            return expectedSeverity == StatusMessageModel.StatusSeverity.Success ? statusElement.Text.Split(" ")[1] : null;
        }

        public async Task FundStoreWallet(WalletId walletId = null, int coins = 1, decimal denomination = 1m)
        {
            walletId ??= WalletId;
            GoToWallet(walletId, WalletsNavPages.Receive);
            Driver.FindElement(By.Id("generateButton")).Click();
            var addressStr = Driver.FindElement(By.Id("address")).GetProperty("value");
            var address = BitcoinAddress.Create(addressStr, ((BTCPayNetwork)Server.NetworkProvider.GetNetwork(walletId.CryptoCode)).NBitcoinNetwork);
            for (var i = 0; i < coins; i++)
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

            GoToWallet(walletId);
            Driver.FindElement(By.Id("bip21parse")).Click();
            Driver.SwitchTo().Alert().SendKeys(bip21);
            Driver.SwitchTo().Alert().Accept();
            Driver.FindElement(By.Id("SignTransaction")).Click();
            Driver.FindElement(By.Id("SignWithSeed")).Click();
            Driver.FindElement(By.CssSelector("button[value=broadcast]")).Click();
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
            //                TestLogs.LogInformation("JavaScript error(s):" + Environment.NewLine + jsErrors.Aggregate("", (s, entry) => s + entry.Message + Environment.NewLine));
            //            }
            //            Assert.Empty(jsErrors);

        }

        public void GoToWallet(WalletId walletId = null, WalletsNavPages navPages = WalletsNavPages.Send)
        {
            walletId ??= WalletId;
            Driver.Navigate().GoToUrl(new Uri(ServerUri, $"wallets/{walletId}"));
            if (navPages != WalletsNavPages.Transactions)
            {
                Driver.FindElement(By.Id($"SectionNav-{navPages}")).Click();
            }
        }

        public void GoToUrl(string relativeUrl)
        {
            Driver.Navigate().GoToUrl(new Uri(ServerUri, relativeUrl));
        }

        public void GoToServer(ServerNavPages navPages = ServerNavPages.Index)
        {
            Driver.FindElement(By.Id("Nav-ServerSettings")).Click();
            if (navPages != ServerNavPages.Index)
            {
                Driver.FindElement(By.Id($"SectionNav-{navPages}")).Click();
            }
        }
    }
}
