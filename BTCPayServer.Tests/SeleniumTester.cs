using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBitcoin.RPC;
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

            var chromeDriverPath = config["ChromeDriverDirectory"] ?? (Server.PayTester.InContainer ? "/usr/bin" : TestUtils.TestDirectory);

            var options = new ChromeOptions();
            if (!runInBrowser)
            {
                options.AddArguments("headless");
            }
            options.AddArguments($"window-size={windowSize.Width}x{windowSize.Height}");
            options.AddArgument("shm-size=2g");
            options.AddArgument("start-maximized");
            options.AddArgument("disable-search-engine-choice-screen");
            if (Server.PayTester.InContainer)
            {
                // Shot in the dark to fix https://stackoverflow.com/questions/53902507/unknown-error-session-deleted-because-of-page-crash-from-unknown-error-cannot
                options.AddArgument("--disable-dev-shm-usage");
                Driver = new OpenQA.Selenium.Remote.RemoteWebDriver(new Uri("http://selenium:4444/wd/hub"), new RemoteSessionSettings(options));
                var containerIp = File.ReadAllText("/etc/hosts").Split('\n', StringSplitOptions.RemoveEmptyEntries).Last()
                    .Split('\t', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                TestLogs.LogInformation($"Selenium: Container's IP {containerIp}");
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
            }

            ServerUri = Server.PayTester.ServerUri;
            Driver.Manage().Window.Maximize();

            TestLogs.LogInformation($"Selenium: Using {Driver.GetType()}");
            TestLogs.LogInformation($"Selenium: Browsing to {ServerUri}");
            TestLogs.LogInformation($"Selenium: Resolution {Driver.Manage().Window.Size}");
            GoToRegister();
            Driver.AssertNoError();
        }

        public void PayInvoice(bool mine = false, decimal? amount = null)
        {
            if (amount is not null)
            {
				try
				{
					Driver.FindElement(By.Id("test-payment-amount")).Clear();
				}
				// Sometimes the element is not available after a window switch... retry
				catch (StaleElementReferenceException)
				{
					Driver.FindElement(By.Id("test-payment-amount")).Clear();
				}
                Driver.FindElement(By.Id("test-payment-amount")).SendKeys(amount.ToString());
            }
            Driver.WaitUntilAvailable(By.Id("FakePayment"));
            Driver.FindElement(By.Id("FakePayment")).Click();
            TestUtils.Eventually(() =>
            {
                Driver.WaitForElement(By.Id("CheatSuccessMessage"));
            });
            if (mine)
            {
                MineBlockOnInvoiceCheckout();
            }
        }

        public void MineBlockOnInvoiceCheckout()
        {
retry:
            try
            {
                Driver.FindElement(By.CssSelector("#mine-block button")).Click();
            }
            catch (StaleElementReferenceException)
            {
                goto retry;
            }
        }

        /// <summary>
        /// Use this ServerUri when trying to browse with selenium
        /// Because for some reason, the selenium container can't resolve the tests container domain name
        /// </summary>
        public Uri ServerUri;
        public IWebElement FindAlertMessage(StatusMessageModel.StatusSeverity severity = StatusMessageModel.StatusSeverity.Success)
        {
            return FindAlertMessage(new[] { severity });
        }
        public IWebElement FindAlertMessage(params StatusMessageModel.StatusSeverity[] severity)
        {
            int retry = 0;
            retry:
            try
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
            // Selenium sometimes sucks...
            catch (StaleElementReferenceException) when (retry < 5)
            {
                retry++;
                goto retry;
            }
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
            CreatedUser = usr;
            Password = "123456";
            IsAdmin = isAdmin;
            return usr;
        }
        string CreatedUser;

        public string Password { get; private set; }
        public bool IsAdmin { get; private set; }

        public TestAccount AsTestAccount()
        {
            return new TestAccount(Server) { StoreId = StoreId, Email = CreatedUser, Password = Password, RegisterDetails = new Models.AccountViewModels.RegisterViewModel() { Password = "123456", Email = CreatedUser }, IsAdmin = IsAdmin };
        }

        public (string storeName, string storeId) CreateNewStore(bool keepId = true)
        {
            // If there's no store yet, there is no dropdown toggle
            if (Driver.PageSource.Contains("id=\"StoreSelectorToggle\""))
            {
                Driver.FindElement(By.Id("StoreSelectorToggle")).Click();
            }
            GoToUrl("/stores/create");
            var name = "Store" + RandomUtils.GetUInt64();
            TestLogs.LogInformation($"Created store {name}");
            Driver.WaitForElement(By.Id("Name")).SendKeys(name);
            var rateSource = new SelectElement(Driver.FindElement(By.Id("PreferredExchange")));
            Assert.Equal("Recommendation (Kraken)", rateSource.SelectedOption.Text);
            rateSource.SelectByText("CoinGecko");
            Driver.WaitForElement(By.Id("Create")).Click();
            Driver.FindElement(By.Id("menu-item-General")).Click();
            var storeId = Driver.WaitForElement(By.Id("Id")).GetAttribute("value");
            if (keepId)
                StoreId = storeId;
            return (name, storeId);
        }

        public Mnemonic GenerateWallet(string cryptoCode = "BTC", string seed = "", bool? importkeys = null, bool isHotWallet = false, ScriptPubKeyType format = ScriptPubKeyType.Segwit)
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
                Driver.SetCheckbox(By.Id("SavePrivateKeys"), isHotWallet);
            }
            else
            {
                var option = isHotWallet ? "Hotwallet" : "Watchonly";
                TestLogs.LogInformation($"Generating new seed ({option})");
                Driver.FindElement(By.Id("GenerateWalletLink")).Click();
                Driver.FindElement(By.Id($"Generate{option}Link")).Click();
            }

            Driver.FindElement(By.Id("ScriptPubKeyType")).Click();
            Driver.FindElement(By.CssSelector($"#ScriptPubKeyType option[value={format}]")).Click();

            Driver.ToggleCollapse("AdvancedSettings");
            if (importkeys is bool v)
                Driver.SetCheckbox(By.Id("ImportKeysToRPC"), v);
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
        public void AddDerivationScheme(string cryptoCode = "BTC", string derivationScheme = "tpubD6NzVbkrYhZ4XxNXjYTcRujMc8z8734diCthtFGgDMimbG5hUsKBuSTCuUyxWL7YwP7R4A5StMTRQiZnb6vE4pdHWPgy9hbiHuVJfBMumUu-[legacy]")
        {
            if (!Driver.PageSource.Contains($"Setup {cryptoCode} Wallet"))
            {
                GoToWalletSettings(cryptoCode);
            }

            Driver.FindElement(By.Id("ImportWalletOptionsLink")).Click();
            Driver.FindElement(By.Id("ImportXpubLink")).Click();
            Driver.FindElement(By.Id("DerivationScheme")).SendKeys(derivationScheme);
            Driver.FindElement(By.Id("Continue")).Click();
            Driver.FindElement(By.Id("Confirm")).Click();
            FindAlertMessage();
        }

        public void AddLightningNode()
        {
            AddLightningNode(null, true);
        }

        public void AddLightningNode(string connectionType = null, bool test = true)
        {
            var cryptoCode = "BTC";
            if (!Driver.PageSource.Contains("Connect to a Lightning node"))
            {
                GoToLightningSettings();
            }

            var connectionString = connectionType switch
            {
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

            ClickPagePrimary();
            Assert.Contains($"{cryptoCode} Lightning node updated.", FindAlertMessage().Text);

            var enabled = Driver.FindElement(By.Id($"{cryptoCode}LightningEnabled"));
            if (enabled.Selected == false)
            {
                enabled.Click();
                ClickPagePrimary();
                Assert.Contains($"{cryptoCode} Lightning settings successfully updated", FindAlertMessage().Text);
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
            if (Driver.PageSource.Contains("id=\"SkipWizard\""))
            {
                Driver.FindElement(By.Id("SkipWizard")).Click();
            }
        }

        public void Logout()
        {
            if (!Driver.PageSource.Contains("id=\"Nav-Logout\""))
                GoToUrl("/account");
            Driver.FindElement(By.Id("menu-item-Account")).Click();
            Driver.FindElement(By.Id("Nav-Logout")).Click();
        }

        public void LogIn()
        {
            LogIn(CreatedUser, "123456");
        }
        public void LogIn(string user, string password = "123456")
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
            {
                GoToUrl($"/stores/{storeId}/");
                StoreId = storeId;
                if (WalletId != null)
                    WalletId = new WalletId(storeId, WalletId.CryptoCode);
            }

            if (storeNavPage != StoreNavPages.General)
            {
                Driver.FindElement(By.Id($"menu-item-{StoreNavPages.General}")).Click();
            }
            Driver.FindElement(By.Id($"menu-item-{storeNavPage}")).Click();
        }

        public void GoToWalletSettings(string cryptoCode = "BTC")
        {
            Driver.FindElement(By.CssSelector($"[data-testid=\"Wallet-{cryptoCode}\"] a")).Click();
            if (Driver.PageSource.Contains($"id=\"menu-item-Settings-{cryptoCode}\""))
            {
                Driver.FindElement(By.Id($"menu-item-Settings-{cryptoCode}")).Click();
            }
        }

        public void GoToLightningSettings(string cryptoCode = "BTC")
        {
            Driver.FindElement(By.CssSelector($"[data-testid=\"Lightning-{cryptoCode}\"]")).Click();
            // if Lightning is already set up we need to navigate to the settings
            if (Driver.PageSource.Contains($"id=\"menu-item-LightningSettings-{cryptoCode}\""))
            {
                Driver.FindElement(By.Id($"menu-item-LightningSettings-{cryptoCode}")).Click();
            }
        }

        public void SelectStoreContext(string storeId)
        {
            Driver.FindElement(By.Id("StoreSelectorToggle")).Click();
            Driver.FindElement(By.Id($"StoreSelectorMenuItem-{storeId}")).Click();
        }

        public void GoToInvoiceCheckout(string invoiceId = null)
        {
            invoiceId ??= InvoiceId;
            Driver.FindElement(By.Id("menu-item-Invoices")).Click();
            Driver.FindElement(By.Id($"invoice-checkout-{invoiceId}")).Click();
            CheckForJSErrors();
            Driver.WaitUntilAvailable(By.Id("Checkout"));
        }

        public void GoToInvoice(string id)
        {
            GoToUrl($"/invoices/{id}/");
        }

        public void GoToInvoices(string storeId = null)
        {
            if (storeId is null)
            {
                Driver.FindElement(By.Id("menu-item-Invoices")).Click();
            }
            else
            {
                GoToUrl(storeId == null ? "/invoices/" : $"/stores/{storeId}/invoices/");
                StoreId = storeId;
            }
        }

        public void GoToProfile(ManageNavPages navPages = ManageNavPages.Index)
        {
            Driver.WaitForAndClick(By.Id("menu-item-Account"));
            Driver.WaitForAndClick(By.Id("Nav-ManageAccount"));
            if (navPages != ManageNavPages.Index)
            {
                Driver.WaitForAndClick(By.Id($"menu-item-{navPages.ToString()}"));
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
            StatusMessageModel.StatusSeverity expectedSeverity = StatusMessageModel.StatusSeverity.Success
        )
        {
            return CreateInvoice(null, amount, currency, refundEmail, defaultPaymentMethod, expectedSeverity);
        }

        public string CreateInvoice(
            string storeId,
            decimal? amount = 100,
            string currency = "USD",
            string refundEmail = "",
            string defaultPaymentMethod = null,
            StatusMessageModel.StatusSeverity expectedSeverity = StatusMessageModel.StatusSeverity.Success
        )
        {
            GoToInvoices(storeId);

            ClickPagePrimary();
            if (amount is decimal v)
                Driver.FindElement(By.Id("Amount")).SendKeys(v.ToString(CultureInfo.InvariantCulture));
            var currencyEl = Driver.FindElement(By.Id("Currency"));
            currencyEl.Clear();
            currencyEl.SendKeys(currency);
            Driver.FindElement(By.Id("BuyerEmail")).SendKeys(refundEmail);
            if (defaultPaymentMethod is not null)
                new SelectElement(Driver.FindElement(By.Name("DefaultPaymentMethod"))).SelectByValue(defaultPaymentMethod);
            ClickPagePrimary();

            var statusElement = FindAlertMessage(expectedSeverity);
            var inv = expectedSeverity == StatusMessageModel.StatusSeverity.Success ? statusElement.Text.Split(" ")[1] : null;
            InvoiceId = inv;
            TestLogs.LogInformation($"Created invoice {inv}");
            return inv;
        }
        string InvoiceId;

        public async Task<string> FundStoreWallet(WalletId walletId = null, int coins = 1, decimal denomination = 1m)
        {
            walletId ??= WalletId;
            GoToWallet(walletId, WalletsNavPages.Receive);
            var addressStr = Driver.FindElement(By.Id("Address")).GetAttribute("data-text");
            var address = BitcoinAddress.Create(addressStr, ((BTCPayNetwork)Server.NetworkProvider.GetNetwork(walletId.CryptoCode)).NBitcoinNetwork);
            for (var i = 0; i < coins; i++)
            {
                bool mined = false;
retry:
                try
                {
                    await Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(denomination));
                }
                catch (RPCException) when (!mined)
                {
                    mined = true;
                    await Server.ExplorerNode.GenerateAsync(1);
                    goto retry;
                }
            }
            Driver.Navigate().Refresh();
            Driver.FindElement(By.Id("CancelWizard")).Click();
            return addressStr;
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
            if (navPages == WalletsNavPages.PSBT)
            {
                Driver.FindElement(By.Id($"menu-item-Send-{walletId.CryptoCode}")).Click();
                Driver.FindElement(By.Id("PSBT")).Click();
            }
            else if (navPages != WalletsNavPages.Transactions)
            {
                Driver.FindElement(By.Id($"menu-item-{navPages}-{walletId.CryptoCode}")).Click();
            }
        }

        public void GoToUrl(string relativeUrl)
        {
            Driver.Navigate().GoToUrl(new Uri(ServerUri, relativeUrl));
        }

        public void GoToServer(ServerNavPages navPages = ServerNavPages.Policies)
        {
            Driver.FindElement(By.Id("menu-item-Policies")).Click();
            if (navPages != ServerNavPages.Policies)
            {
                Driver.FindElement(By.Id($"menu-item-{navPages}")).Click();
            }
        }

        public void AddUserToStore(string storeId, string email, string role)
        {
            if (Driver.FindElements(By.Id("AddUser")).Count == 0)
            {
                GoToStore(storeId, StoreNavPages.Users);
            }
            Driver.FindElement(By.Id("Email")).SendKeys(email);
            new SelectElement(Driver.FindElement(By.Id("Role"))).SelectByValue(role);
            Driver.FindElement(By.Id("AddUser")).Click();
            Assert.Contains("The user has been added successfully", FindAlertMessage().Text);
        }

        public void AssertPageAccess(bool shouldHaveAccess, string url)
        {
            GoToUrl(url);
            Assert.DoesNotMatch("404 - Page not found</h", Driver.PageSource);
            if (shouldHaveAccess)
            {
                Assert.DoesNotMatch("- Denied</h", Driver.PageSource);
                // check associated link is active if present
                var sidebarLink = Driver.FindElements(By.CssSelector($"#mainNav a[href=\"{url}\"]")).FirstOrDefault();
                if (sidebarLink != null)
                {
                    Assert.Contains("active", sidebarLink.GetAttribute("class"));
                }
            }
            else
                Assert.Contains("- Denied</h", Driver.PageSource);
        }

        public (string appName, string appId) CreateApp(string type, string name = null)
        {
            if (string.IsNullOrEmpty(name))
                name = $"{type}-{Guid.NewGuid().ToString()[..14]}";
            Driver.FindElement(By.Id($"menu-item-CreateApp-{type}")).Click();
            Driver.FindElement(By.Name("AppName")).SendKeys(name);
            ClickPagePrimary();
            Assert.Contains("App successfully created", FindAlertMessage().Text);
            var appId = Driver.Url.Split('/')[4];
            return (name, appId);
        }

        public void ClickPagePrimary()
        {
            try
            {
                Driver.FindElement(By.Id("page-primary")).Click();
            }
            catch (NoSuchElementException)
            {
                Driver.WaitForAndClick(By.Id("page-primary"));
            }
        }

        public void ClickCancel()
        {
            Driver.FindElement(By.Id("CancelWizard")).Click();
        }
    }
}
