using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using BTCPayServer.BIP78.Sender;
using PlaywrightSharp;
using PlaywrightSharp.Chromium;
using Xunit;

namespace BTCPayServer.Tests
{
    public class PlayWrightTester : IDisposable
    {
        public IPage Page { get; set; }
        public ServerTester Server { get; set; }
        public WalletId WalletId { get; set; }

        public string StoreId { get; set; }

        public static SeleniumTester Create([CallerMemberNameAttribute] string scope = null, bool newDb = false) =>
            new SeleniumTester { Server = ServerTester.Create(scope, newDb) };

        public static readonly TimeSpan ImplicitWait = TimeSpan.FromSeconds(5);

        public async Task StartAsync()
        {
            await Server.StartAsync();

            var windowSize = (Width: 1200, Height: 1000);
            var builder = new ConfigurationBuilder();
            builder.AddUserSecrets("AB0AC1DD-9D26-485B-9416-56A33F268117");
            var config = builder.Build();
            this.PlayWright = await Playwright.CreateAsync();
            // Run `dotnet user-secrets set RunSeleniumInBrowser true` to run tests in browser
            var runInBrowser = config["RunSeleniumInBrowser"] == "true";
            // Reset this using `dotnet user-secrets remove RunSeleniumInBrowser`

            // var chromeDriverPath = config["ChromeDriverDirectory"] ?? (Server.PayTester.InContainer ? "/usr/bin" : Directory.GetCurrentDirectory());

            var options = new List<string>();
           
            // options.Add($"window-size={windowSize.Width}x{windowSize.Height}");
            // options.Add("shm-size=2g");


            this.Browser = await this.PlayWright.Chromium.LaunchAsync(
                headless:!runInBrowser, 
                chromiumSandbox:!Server.PayTester.InContainer,
                args:options.ToArray()
                );

            this.Context = await Browser.NewContextAsync(new ViewportSize()
            {
                Width = windowSize.Width, Height = windowSize.Height
            });
            
            Page = await Context.NewPageAsync();
            // if (runInBrowser)
            // {
            //     // ensure maximized window size
            //     // otherwise TESTS WILL FAIL because of different hierarchy in navigation menu
            //     Browser..Window.Maximize();
            // }

            Logs.Tester.LogInformation($"Selenium: Using {Page.GetType()}");
            Logs.Tester.LogInformation($"Selenium: Browsing to {Server.PayTester.ServerUri}");
            Logs.Tester.LogInformation($"Selenium: Resolution {Page.ViewportSize}");
            await GoToRegister();
            Page.AssertNoError();
        }

        public IChromiumBrowserContext Context { get; set; }

        public IChromiumBrowser Browser { get; set; }

        public IPlaywright PlayWright { get; internal set; }

        internal async Task<IElementHandle> FindAlertMessage(StatusMessageModel.StatusSeverity severity = StatusMessageModel.StatusSeverity.Success)
        {
            var className = $".alert-{StatusMessageModel.ToString(severity)}";
            var el = await Page.QuerySelectorAsync(className) ?? await Page.WaitForSelectorAsync(className);
            if (el is null)
                throw new NoSuchElementException($"Unable to find {className}");
            return el;
        }

        public string Link(string relativeLink)
        {
            return Server.PayTester.ServerUri.AbsoluteUri.WithoutEndingSlash() + relativeLink.WithStartingSlash();
        }

        public async Task GoToRegister()
        {
            await Page.GoToAsync(Link("/register"));
        }

        public async Task<string> RegisterNewUser(bool isAdmin = false)
        {
            var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
            Logs.Tester.LogInformation($"User: {usr} with password 123456");
            await Page.TypeAsync("#Email", usr);
           await Page.TypeAsync("#Password","123456");
           await Page.TypeAsync("#ConfirmPassword", "123456");
           if (isAdmin)
               await Page.CheckAsync("#IsAdmin");
            await Page.ClickAsync("#RegisterButton");
            Page.AssertNoError();
            return usr;
        }

        public async Task<(string storeName, string storeId)> CreateNewStore()
        {
            await Page.ClickAsync( "#Stores");
            await Page.ClickAsync("#CreateStore");
            var name = "Store" + RandomUtils.GetUInt64();
            Page.WaitForElement("#Name")).SendKeys(name);
            Page.WaitForElement("#Create")).Click();
            StoreId = Page.WaitForElement("#Id")).GetAttribute("value");
            return (name, StoreId);
        }

        public Mnemonic GenerateWallet(string cryptoCode = "BTC", string seed = "", bool importkeys = false, bool privkeys = false, ScriptPubKeyType format = ScriptPubKeyType.Segwit)
        {
            Page.FindElement(By.Id($"Modify{cryptoCode}")).Click();

            // Replace previous wallet case
            if (Page.PageSource.Contains("id=\"ChangeWalletLink\""))
            {
                Page.FindElement("#ChangeWalletLink")).Click();
                Page.FindElement("#continue")).Click();
            }

            if (string.IsNullOrEmpty(seed))
            {
                var option = privkeys ? "Hotwallet" : "Watchonly";
                Logs.Tester.LogInformation($"Generating new seed ({option})");
                Page.FindElement("#GenerateWalletLink")).Click();
                Page.FindElement(By.Id($"Generate{option}Link")).Click();
            }
            else
            {
                Logs.Tester.LogInformation("Progressing with existing seed");
                Page.FindElement("#ImportWalletOptionsLink")).Click();
                Page.FindElement("#ImportSeedLink")).Click();
                Page.FindElement("#ExistingMnemonic")).SendKeys(seed);
                Page.SetCheckbox("#SavePrivateKeys"), privkeys);
            }

            Page.FindElement("#ScriptPubKeyType")).Click();
            Page.FindElement(By.CssSelector($"#ScriptPubKeyType option[value={format}]")).Click();

            // Open advanced settings via JS, because if we click the link it triggers the toggle animation.
            // This leads to Selenium trying to click the button while it is moving resulting in an error.
            Page.ExecuteJavaScript("document.getElementById('AdvancedSettings').classList.add('show')");

            Page.SetCheckbox("#ImportKeysToRPC"), importkeys);
            Page.FindElement("#Continue")).Click();

            // Seed backup page
            FindAlertMessage();
            if (string.IsNullOrEmpty(seed))
            {
                seed = Page.FindElements("#RecoveryPhrase")).First().GetAttribute("data-mnemonic");
            }

            // Confirm seed backup
            Page.FindElement("#confirm")).Click();
            Page.FindElement("#submit")).Click();

            WalletId = new WalletId(StoreId, cryptoCode);
            return new Mnemonic(seed);
        }

        public void AddDerivationScheme(string cryptoCode = "BTC", string derivationScheme = "xpub661MyMwAqRbcGABgHMUXDzPzH1tU7eZaAaJQXhDXsSxsqyQzQeU6kznNfSuAyqAK9UaWSaZaMFdNiY5BCF4zBPAzSnwfUAwUhwttuAKwfRX-[legacy]")
        {
            Page.FindElement(By.Id($"Modify{cryptoCode}")).Click();
            Page.FindElement("#ImportWalletOptionsLink")).Click();
            Page.FindElement("#ImportXpubLink")).Click();
            Page.FindElement("#DerivationScheme")).SendKeys(derivationScheme);
            Page.FindElement("#Continue")).Click();
            Page.FindElement("#Confirm")).Click();
            FindAlertMessage();
        }

        public void AddLightningNode(string cryptoCode = "BTC", LightningConnectionType? connectionType = null)
        {
            Page.FindElement(By.Id($"Modify-Lightning{cryptoCode}")).Click();

            var connectionString = connectionType switch
            {
                LightningConnectionType.Charge =>
                    $"type=charge;server={Server.MerchantCharge.Client.Uri.AbsoluteUri};allowinsecure=true",
                LightningConnectionType.CLightning =>
                    $"type=clightning;server={((CLightningClient) Server.MerchantLightningD).Address.AbsoluteUri}",
                LightningConnectionType.LndREST =>
                    $"type=lnd-rest;server={Server.MerchantLnd.Swagger.BaseUrl};allowinsecure=true",
                _ => null
            };

            if (connectionString == null)
            {
                Assert.True(Page.FindElement("#LightningNodeType-Internal")).Enabled, "Usage of the internal Lightning node is disabled.");
                Page.FindElement(By.CssSelector("label[for=\"LightningNodeType-Internal\"]")).Click();
            }
            else
            {
                Page.FindElement(By.CssSelector("label[for=\"LightningNodeType-Custom\"]")).Click();
                Page.FindElement("#ConnectionString")).SendKeys(connectionString);

                Page.FindElement("#test")).Click();
                Assert.Contains("Connection to the Lightning node successful.", FindAlertMessage().Text);
            }

            Page.FindElement("#save")).Click();
            Assert.Contains($"{cryptoCode} Lightning node updated.", FindAlertMessage().Text);

            var enabled = Page.FindElement(By.Id($"{cryptoCode}LightningEnabled"));
            if (enabled.Text == "Enable")
            {
                enabled.Click();
                Assert.Contains($"{cryptoCode} Lightning payments are now enabled for this store.", FindAlertMessage().Text);
            }
        }

        public void ClickOnAllSideMenus()
        {
            var links = Page.FindElements(By.CssSelector(".nav .nav-link")).Select(c => c.GetAttribute("href")).ToList();
            Page.AssertNoError();
            Assert.NotEmpty(links);
            foreach (var l in links)
            {
                Logs.Tester.LogInformation($"Checking no error on {l}");
                Page.Navigate().GoToUrl(l);
                Page.AssertNoError();
            }
        }

        public void Dispose()
        {
            if (Page != null)
            {
                try
                {
                    Page.Quit();
                }
                catch
                {
                    // ignored
                }

                Page.Dispose();
            }

            Server?.Dispose();
        }

        internal void AssertNotFound()
        {
            Assert.Contains("404 - Page not found</h1>", Page.PageSource);
        }

        public void GoToHome()
        {
            Page.Navigate().GoToUrl(Server.PayTester.ServerUri);
        }

        public void Logout()
        {
            Page.FindElement("#Logout")).Click();
        }

        public void Login(string user, string password)
        {
            Page.FindElement("#Email")).SendKeys(user);
            Page.FindElement("#Password")).SendKeys(password);
            Page.FindElement("#LoginButton")).Click();
        }

        public void GoToStores()
        {
            Page.FindElement("#Stores")).Click();
        }

        public void GoToStore(string storeId, StoreNavPages storeNavPage = StoreNavPages.Index)
        {
            Page.FindElement("#Stores")).Click();
            Page.FindElement(By.Id($"update-store-{storeId}")).Click();

            if (storeNavPage != StoreNavPages.Index)
            {
                Page.FindElement(By.Id(storeNavPage.ToString())).Click();
            }
        }

        public void GoToInvoiceCheckout(string invoiceId)
        {
            Page.FindElement("#Invoices")).Click();
            Page.FindElement(By.Id($"invoice-checkout-{invoiceId}")).Click();
            CheckForJSErrors();
        }

        public void GoToInvoices()
        {
            Page.FindElement("#Invoices")).Click();
        }

        public void GoToProfile(ManageNavPages navPages = ManageNavPages.Index)
        {
            Page.FindElement("#MySettings")).Click();
            if (navPages != ManageNavPages.Index)
            {
                Page.FindElement(By.Id(navPages.ToString())).Click();
            }
        }

        public void GoToLogin()
        {
            Page.Navigate().GoToUrl(new Uri(Server.PayTester.ServerUri, "/login"));
        }

        public string CreateInvoice(string storeName, decimal amount = 100, string currency = "USD", string refundEmail = "")
        {
            GoToInvoices();
            Page.FindElement("#CreateNewInvoice")).Click();
            Page.FindElement("#Amount")).SendKeys(amount.ToString(CultureInfo.InvariantCulture));
            var currencyEl = Page.FindElement("#Currency"));
            currencyEl.Clear();
            currencyEl.SendKeys(currency);
            Page.FindElement("#BuyerEmail")).SendKeys(refundEmail);
            Page.FindElement(By.Name("StoreId")).SendKeys(storeName);
            Page.FindElement("#Create")).Click();

            var statusElement = FindAlertMessage();
            var id = statusElement.Text.Split(" ")[1];
            return id;
        }

        public async Task FundStoreWallet(WalletId walletId = null, int coins = 1, decimal denomination = 1m)
        {
            walletId ??= WalletId;
            GoToWallet(walletId, WalletsNavPages.Receive);
            Page.FindElement("#generateButton")).Click();
            var addressStr = Page.FindElement("#address")).GetProperty("value");
            var address = BitcoinAddress.Create(addressStr, ((BTCPayNetwork)Server.NetworkProvider.GetNetwork(walletId.CryptoCode)).NBitcoinNetwork);
            for (var i = 0; i < coins; i++)
            {
                await Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(denomination));
            }
        }

        public void PayInvoice(WalletId walletId, string invoiceId)
        {
            GoToInvoiceCheckout(invoiceId);
            var bip21 = Page.FindElement(By.ClassName("payment__details__instruction__open-wallet__btn"))
                .GetAttribute("href");
            Assert.Contains($"{PayjoinClient.BIP21EndpointKey}", bip21);

            GoToWallet(walletId);
            Page.FindElement("#bip21parse")).Click();
            Page.SwitchTo().Alert().SendKeys(bip21);
            Page.SwitchTo().Alert().Accept();
            Page.FindElement("#SendMenu")).Click();
            Page.FindElement(By.CssSelector("button[value=nbx-seed]")).Click();
            Page.FindElement(By.CssSelector("button[value=broadcast]")).Click();
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
            Page.Navigate().GoToUrl(new Uri(Server.PayTester.ServerUri, $"wallets/{walletId}"));
            if (navPages != WalletsNavPages.Transactions)
            {
                Page.FindElement(By.Id($"Wallet{navPages}")).Click();
            }
        }

        public void GoToUrl(string relativeUrl)
        {
            Page.Navigate().GoToUrl(new Uri(Server.PayTester.ServerUri, relativeUrl));
        }

        public void GoToServer(ServerNavPages navPages = ServerNavPages.Index)
        {
            Page.FindElement("#ServerSettings")).Click();
            if (navPages != ServerNavPages.Index)
            {
                Page.FindElement(By.Id($"Server-{navPages}")).Click();
            }
        }

        public void GoToInvoice(string id)
        {
            GoToInvoices();
            foreach (var el in Page.FindElements(By.ClassName("invoice-details-link")))
            {
                if (el.GetAttribute("href").Contains(id, StringComparison.OrdinalIgnoreCase))
                {
                    el.Click();
                    break;
                }
            }
        }
    }

    public class NoSuchElementException : Exception
    {
        public NoSuchElementException(string message):base(message)
        {
            
        }
    }
}
