using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            await Page.AssertNoError();
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
            await Page.AssertNoError();
            return usr;
        }

        public async Task<(string storeName, string storeId)> CreateNewStore()
        {
            await Page.ClickAsync( "#Stores");
            await Page.ClickAsync("#CreateStore");
            var name = "Store" + RandomUtils.GetUInt64();
            await Page.TypeAsync("#Name", name);
            await Page.ClickAsync("#Create");
            StoreId = await (await Page.QuerySelectorAsync("#Id")).GetAttributeAsync("value");
            return (name, StoreId);
        }

        public async Task<Mnemonic> GenerateWallet(string cryptoCode = "BTC", string seed = "", bool importkeys = false, bool privkeys = false, ScriptPubKeyType format = ScriptPubKeyType.Segwit)
        {
            await Page.ClickAsync($"#Modify{cryptoCode}");

            // Replace previous wallet case
            if ((await Page.GetContentAsync()).Contains("id=\"ChangeWalletLink\""))
            {
                await Page.ClickAsync("#ChangeWalletLink");
                await Page.ClickAsync("#continue");
            }

            if (string.IsNullOrEmpty(seed))
            {
                var option = privkeys ? "Hotwallet" : "Watchonly";
                Logs.Tester.LogInformation($"Generating new seed ({option})");
                await Page.ClickAsync("#GenerateWalletLink");
                await Page.ClickAsync($"#Generate{option}Link");
            }
            else
            {
                Logs.Tester.LogInformation("Progressing with existing seed");
                await Page.ClickAsync("#ImportWalletOptionsLink");
                await Page.ClickAsync("#ImportSeedLink");
                await Page.TypeAsync("#ExistingMnemonic", seed);
                await Page.SetCheckbox("#SavePrivateKeys", privkeys);
            }

            await Page.ClickAsync("#ScriptPubKeyType");
            await Page.ClickAsync($"#ScriptPubKeyType option[value={format}]");

            // Open advanced settings via JS, because if we click the link it triggers the toggle animation.
            // This leads to Selenium trying to click the button while it is moving resulting in an error.
            await Page.EvaluateAsync("document.getElementById('AdvancedSettings').classList.add('show')");

            await Page.SetCheckbox("#ImportKeysToRPC", importkeys);
            await Page.ClickAsync("#Continue");

            // Seed backup page
            await FindAlertMessage();
            if (string.IsNullOrEmpty(seed))
            {
                seed = await (await Page.QuerySelectorAsync("#RecoveryPhrase")).GetAttributeAsync("data-mnemonic");
            }

            // Confirm seed backup
            await Page.ClickAsync("#confirm");
            await Page.ClickAsync("#submit");

            WalletId = new WalletId(StoreId, cryptoCode);
            return new Mnemonic(seed);
        }

        public async Task AddDerivationScheme(string cryptoCode = "BTC", string derivationScheme = "xpub661MyMwAqRbcGABgHMUXDzPzH1tU7eZaAaJQXhDXsSxsqyQzQeU6kznNfSuAyqAK9UaWSaZaMFdNiY5BCF4zBPAzSnwfUAwUhwttuAKwfRX-[legacy]")
        {
            await Page.ClickAsync($"#Modify{cryptoCode}");
            await Page.ClickAsync("#ImportWalletOptionsLink");
            await Page.ClickAsync("#ImportXpubLink");
            await Page.TypeAsync("#DerivationScheme",derivationScheme);
            await Page.ClickAsync("#Continue");
            await Page.ClickAsync("#Confirm");
            await FindAlertMessage();
        }

        public async Task AddLightningNode(string cryptoCode = "BTC", LightningConnectionType? connectionType = null)
        {
            await Page.ClickAsync($"#Modify-Lightning{cryptoCode}");

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
                Assert.True(await (await Page.QuerySelectorAsync("#LightningNodeType-Internal")).IsEnabledAsync(), "Usage of the internal Lightning node is disabled.");
                await Page.ClickAsync("label[for=\"LightningNodeType-Internal\"]");
            }
            else
            {
                await Page.ClickAsync("label[for=\"LightningNodeType-Custom\"]");
                await Page.TypeAsync("#ConnectionString", connectionString);

                await Page.ClickAsync("#test");
                Assert.Contains("Connection to the Lightning node successful.", await (await FindAlertMessage()).GetTextContentAsync());
            }

            await Page.ClickAsync("#save");
            Assert.Contains($"{cryptoCode} Lightning node updated.", await (await FindAlertMessage()).GetTextContentAsync());

            var enabled = await Page.QuerySelectorAsync($"#{cryptoCode}LightningEnabled");
            if (await enabled.GetTextContentAsync() == "Enable")
            {
                await enabled.ClickAsync();
                Assert.Contains($"{cryptoCode} Lightning payments are now enabled for this store.", await (await FindAlertMessage()).GetTextContentAsync());
            }
        }

        public async Task ClickOnAllSideMenus()
        {
            var links = await Task.WhenAll((await Page
                    .QuerySelectorAllAsync(".nav .nav-link")).Select(handle => handle.GetAttributeAsync("href")));
            await Page.AssertNoError();
            Assert.NotEmpty(links);
            foreach (var l in links)
            {
                Logs.Tester.LogInformation($"Checking no error on {l}");
                await Page.GoToAsync(l);
                await Page.AssertNoError();
            }
        }

        public void Dispose()
        {
            Page.CloseAsync().GetAwaiter().GetResult();
            PlayWright.Dispose();
            Server?.Dispose();
        }

        internal async Task AssertNotFound()
        {
            Assert.Contains("404 - Page not found</h1>", await Page.GetContentAsync());
        }

        public async Task GoToHome()
        {
            await Page.GoToAsync(Server.PayTester.ServerUri.ToString());
        }

        public async Task Logout()
        {
           await  Page.ClickAsync("#Logout");
        }

        public async Task Login(string user, string password)
        {
            await Page.TypeAsync("#Email", user);
            await Page.TypeAsync("#Password",password);
            await Page.ClickAsync("#LoginButton");
        }

        public async Task GoToStores()
        {
           await  Page.ClickAsync("#Stores");
        }

        public async Task GoToStore(string storeId, StoreNavPages storeNavPage = StoreNavPages.Index)
        {
            await Page.ClickAsync("#Stores");
            await Page.ClickAsync($"#update-store-{storeId}");

            if (storeNavPage != StoreNavPages.Index)
            {
                await Page.ClickAsync($"#{storeNavPage}");
            }
        }

        public async Task GoToInvoiceCheckout(string invoiceId)
        {
            await Page.ClickAsync("#Invoices");
            await Page.ClickAsync($"#invoice-checkout-{invoiceId}");
            await CheckForJSErrors();
        }

        public async Task GoToInvoices()
        {
            await Page.ClickAsync("#Invoices");
        }

        public async Task GoToProfile(ManageNavPages navPages = ManageNavPages.Index)
        {
            await Page.ClickAsync("#MySettings");
            if (navPages != ManageNavPages.Index)
            {
                await Page.ClickAsync($"#{navPages}");
            }
        }

        public async Task GoToLogin()
        {
            await Page.GoToAsync(new Uri(Server.PayTester.ServerUri, "/login").ToString());
        }

        public async Task<string> CreateInvoice(string storeName, decimal amount = 100, string currency = "USD", string refundEmail = "")
        {
            await GoToInvoices();
            await Page.ClickAsync("#CreateNewInvoice");
            await Page.TypeAsync("#Amount", amount.ToString(CultureInfo.InvariantCulture));
            var currencyEl = await Page.QuerySelectorAsync("#Currency");
            await currencyEl.FillAsync("");
            await currencyEl.TypeAsync(currency);
            await Page.TypeAsync("#BuyerEmail",refundEmail);
            await Page.TypeAsync("[name='StoreId']",storeName);
            await Page.ClickAsync("#Create");

            var statusElement = await FindAlertMessage();
            var id = (await statusElement.GetTextContentAsync()).Split(" ")[1];
            return id;
        }

        public async Task FundStoreWallet(WalletId walletId = null, int coins = 1, decimal denomination = 1m)
        {
            walletId ??= WalletId;
            await GoToWallet(walletId, WalletsNavPages.Receive);
            await Page.ClickAsync("#generateButton");
            var addressStr = await Page.GetAttributeAsync("#address", "value");
            var address = BitcoinAddress.Create(addressStr, ((BTCPayNetwork)Server.NetworkProvider.GetNetwork(walletId.CryptoCode)).NBitcoinNetwork);
            for (var i = 0; i < coins; i++)
            {
                await Server.ExplorerNode.SendToAddressAsync(address, Money.Coins(denomination));
            }
        }
        
        

        public async Task PayInvoice(WalletId walletId, string invoiceId)
        {
            await GoToInvoiceCheckout(invoiceId);
            var bip21 = await Page.GetAttributeAsync(".payment__details__instruction__open-wallet__btn" , "href");
            Assert.Contains($"{PayjoinClient.BIP21EndpointKey}", bip21);

            GoToWallet(walletId);
            var tcs = new TaskCompletionSource<bool>();
            Page.Dialog += async (_, dialog) =>
            {
                Assert.Equal(dialog.Dialog.Type, DialogType.Prompt);
                await dialog.Dialog.AcceptAsync(bip21);
                tcs.SetResult(true);
            };
            await Page.ClickAsync("#bip21parse");
            await tcs.Task;
            await Page.ClickAsync("#SendMenu");
            await Page.ClickAsync("button[value=nbx-seed]");
            await Page.ClickAsync("button[value=broadcast]");
        }

        private async Task CheckForJSErrors()
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

        public async Task GoToWallet(WalletId walletId = null, WalletsNavPages navPages = WalletsNavPages.Send)
        {
            walletId ??= WalletId;
            await Page.GoToAsync(new Uri(Server.PayTester.ServerUri, $"wallets/{walletId}").ToString());
            if (navPages != WalletsNavPages.Transactions)
            {
                await Page.ClickAsync($"#Wallet{navPages}");
            }
        }

        public async Task GoToUrl(string relativeUrl)
        {
            await Page.GoToAsync(new Uri(Server.PayTester.ServerUri, relativeUrl).ToString());
        }

        public async Task GoToServer(ServerNavPages navPages = ServerNavPages.Index)
        {
            await Page.ClickAsync("#ServerSettings");
            if (navPages != ServerNavPages.Index)
            {
                await Page.ClickAsync($"#Server-{navPages}");
            }
        }

        public async Task GoToInvoice(string id)
        {
            await GoToInvoices();
            foreach (var el in await Page.QuerySelectorAllAsync(".invoice-details-link"))
            {
                if ((await el.GetAttributeAsync("href")).Contains(id, StringComparison.OrdinalIgnoreCase))
                {
                    await el.ClickAsync();
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
