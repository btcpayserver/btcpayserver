using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using NBitcoin;
using OpenQA.Selenium;
using Xunit;

namespace BTCPayServer.Tests
{
    public class PlaywrightTester : IDisposable
    {
        public Uri ServerUri;
        private string CreatedUser;
        private string InvoiceId;
        public Logging.ILog TestLogs => Server.TestLogs;
        public IPage Page { get; set; }
        public IBrowser Browser { get; private set; }
        public ServerTester Server { get; set; }
        public WalletId WalletId { get; set; }
        public string Password { get; private set; }
        public string StoreId { get; private set; }
        public bool IsAdmin { get; private set; }

        public static readonly TimeSpan ImplicitWait = TimeSpan.FromSeconds(5);

        public async Task StartAsync()
        {
            Server.PayTester.NoCSP = true;
            await Server.StartAsync();
            var builder = new ConfigurationBuilder();
            builder.AddUserSecrets("AB0AC1DD-9D26-485B-9416-56A33F268117");
            var config = builder.Build();

            var playwright = await Playwright.CreateAsync();
            Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = Server.PayTester.InContainer,
                SlowMo = Server.PayTester.InContainer ? 0 : 50, // Add slight delay, nicer during dev
            });
            var context = await Browser.NewContextAsync();
            Page = await context.NewPageAsync();
            ServerUri = Server.PayTester.ServerUri;
            TestLogs.LogInformation($"Playwright: Using {Page.GetType()}");
            TestLogs.LogInformation($"Playwright: Browsing to {ServerUri}");
            await GoToRegister();
            await Page.AssertNoError();
        }

        public async Task PayInvoiceAsync(IPage page, bool mine = false, decimal? amount = null)
        {
            if (amount is not null)
            {
                try
                {
                    await page.Locator("#test-payment-amount").ClearAsync();
                }
                catch (PlaywrightException)
                {
                    await page.Locator("#test-payment-amount").ClearAsync();
                }
                await page.Locator("#test-payment-amount").FillAsync(amount.ToString());
            }
            await page.Locator("#FakePayment").WaitForAsync();
            await page.Locator("#FakePayment").ClickAsync();
            await TestUtils.EventuallyAsync(async () =>
            {
                await page.Locator("#CheatSuccessMessage").WaitForAsync();
            });
            if (mine)
            {
                await MineBlockOnInvoiceCheckout(page);
            }
        }

        public async Task GoToInvoices(string storeId = null)
        {
            if (storeId is null)
            {
                await Page.Locator("#StoreNav-Invoices").ClickAsync();
            }
            else
            {
                await GoToUrl(storeId == null ? "/invoices/" : $"/stores/{storeId}/invoices/");
                StoreId = storeId;
            }
        }

        public async Task<string> CreateInvoice(decimal? amount = 10, string currency = "USD",
            string refundEmail = "", string defaultPaymentMethod = null,
            StatusMessageModel.StatusSeverity expectedSeverity = StatusMessageModel.StatusSeverity.Success)
        {
            return await CreateInvoice(null, amount, currency, refundEmail, defaultPaymentMethod, expectedSeverity);
        }

        public async Task<string> CreateInvoice(string storeId, decimal? amount = 10, string currency = "USD",
            string refundEmail = "", string defaultPaymentMethod = null,
            StatusMessageModel.StatusSeverity expectedSeverity = StatusMessageModel.StatusSeverity.Success)
        {
            await GoToInvoices(storeId);

            await ClickPagePrimary();
            if (amount is decimal v)
                await Page.Locator("#Amount").FillAsync(v.ToString(CultureInfo.InvariantCulture));

            var currencyEl = Page.Locator("#Currency");
            await currencyEl.ClearAsync();
            await currencyEl.FillAsync(currency);
            await Page.Locator("#BuyerEmail").FillAsync(refundEmail);
            if (defaultPaymentMethod is not null)
                await Page.SelectOptionAsync("select[name='DefaultPaymentMethod']", new SelectOptionValue { Value = defaultPaymentMethod });
            await ClickPagePrimary();

            var statusText = (await FindAlertMessage(expectedSeverity)).TextContentAsync();
            var inv = expectedSeverity == StatusMessageModel.StatusSeverity.Success
                ? Regex.Match(await statusText, @"Invoice (\w+) just created!").Groups[1].Value
                : null;

            InvoiceId = inv;
            TestLogs.LogInformation($"Created invoice {inv}");
            return inv;
        }

        public async Task GoToInvoiceCheckout(string invoiceId = null)
        {
            invoiceId ??= InvoiceId;
            await Page.Locator("#StoreNav-Invoices").ClickAsync();
            await Page.Locator($"#invoice-checkout-{invoiceId}").ClickAsync();
            await Page.Locator("#Checkout").WaitForAsync();
        }

        public async Task GoToWallet(WalletId walletId = null, WalletsNavPages navPages = WalletsNavPages.Send)
        {
            walletId ??= WalletId;
            await GoToUrl($"wallets/{walletId}");
            if (navPages == WalletsNavPages.PSBT)
            {
                await Page.Locator("#WalletNav-Send").ClickAsync();
                await Page.Locator("#PSBT").ClickAsync();
            }
            else if (navPages != WalletsNavPages.Transactions)
            {
                await Page.Locator($"#WalletNav-{navPages}").ClickAsync();
            }
        }

        public async Task MineBlockOnInvoiceCheckout(IPage page)
        {
        retry:
            try { await page.Locator("#mine-block button").ClickAsync(); }
            catch (PlaywrightException) { goto retry; }
        }

        public async Task<ILocator> FindAlertMessage(StatusMessageModel.StatusSeverity severity = StatusMessageModel.StatusSeverity.Success, string partialText = null)
        {
            var locator = await FindAlertMessage(new[] { severity });
            if (partialText is not null)
            {
                var txt = await locator.TextContentAsync();
                Assert.Contains(partialText, txt);
            }
            return locator;
        }

        public async Task<ILocator> FindAlertMessage(params StatusMessageModel.StatusSeverity[] severity)
        {
            var className = string.Join(", ", severity.Select(statusSeverity => $".alert-{StatusMessageModel.ToString(statusSeverity)}"));
            var locator = Page.Locator(className);
            try
            {
                await locator.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
                var visibleElements = await locator.AllAsync();
                var visibleElement = visibleElements.FirstOrDefault(el => el.IsVisibleAsync().GetAwaiter().GetResult());
                if (visibleElement != null)
                    return Page.Locator(className).First;

                return locator.First;
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Unable to find {className}");
            }
        }

        public async Task GoToUrl(string relativeUrl)
        {
            await Page.GotoAsync(Link(relativeUrl), new() { WaitUntil  = WaitUntilState.Commit } );
        }

        public string Link(string relativeLink)
        {
            return ServerUri.AbsoluteUri.WithoutEndingSlash() + relativeLink.WithStartingSlash();
        }

        public async Task<string> RegisterNewUser(bool isAdmin = false)
        {
            var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
            await Page.FillAsync("#Email", usr);
            await Page.FillAsync("#Password", "123456");
            await Page.FillAsync("#ConfirmPassword", "123456");
            if (isAdmin)
                await Page.ClickAsync("#IsAdmin");

            await Page.ClickAsync("#RegisterButton");
            CreatedUser = usr;
            Password = "123456";
            IsAdmin = isAdmin;
            return usr;
        }

        public TestAccount AsTestAccount()
        {
            return new TestAccount(Server) { StoreId = StoreId, Email = CreatedUser, Password = Password, RegisterDetails = new Models.AccountViewModels.RegisterViewModel() { Password = "123456", Email = CreatedUser }, IsAdmin = IsAdmin };
        }

        public async Task<(string storeName, string storeId)> CreateNewStore(bool keepId = true)
        {
            if (await Page.Locator("#StoreSelectorToggle").IsVisibleAsync())
            {
                await Page.Locator("#StoreSelectorToggle").ClickAsync();
            }
            await GoToUrl("/stores/create");
            var name = "Store" + RandomUtils.GetUInt64();
            TestLogs.LogInformation($"Created store {name}");
            await Page.FillAsync("#Name", name);

            var selectedOption = await Page.Locator("#PreferredExchange option:checked").TextContentAsync();
            Assert.Equal("Recommendation (Kraken)", selectedOption.Trim());
            await Page.Locator("#PreferredExchange").SelectOptionAsync(new SelectOptionValue { Label = "CoinGecko" });
            await Page.ClickAsync("#Create");
            await Page.ClickAsync("#StoreNav-General");
            var storeId = await Page.InputValueAsync("#Id");
            if (keepId)
                StoreId = storeId;

            return (name, storeId);
        }

        public async Task<Mnemonic> GenerateWallet(string cryptoCode = "BTC", string seed = "", bool? importkeys = null, bool isHotWallet = false, ScriptPubKeyType format = ScriptPubKeyType.Segwit)
        {
            var isImport = !string.IsNullOrEmpty(seed);
            await GoToWalletSettings(cryptoCode);
            // Replace previous wallet case
            if (await Page.Locator("#ChangeWalletLink").IsVisibleAsync())
            {
                await Page.Locator("#ActionsDropdownToggle").ClickAsync();
                await Page.Locator("#ChangeWalletLink").ClickAsync();
                await Page.Locator("#ConfirmInput").FillAsync("REPLACE");
                await Page.Locator("#ConfirmContinue").ClickAsync();
            }

            if (isImport)
            {
                TestLogs.LogInformation("Progressing with existing seed");
                await Page.Locator("#ImportWalletOptionsLink").ClickAsync();
                await Page.Locator("#ImportSeedLink").ClickAsync();
                await Page.Locator("#ExistingMnemonic").FillAsync(seed);
                await Page.Locator("#SavePrivateKeys").SetCheckedAsync(isHotWallet);
            }
            else
            {
                var option = isHotWallet ? "Hotwallet" : "Watchonly";
                TestLogs.LogInformation($"Generating new seed ({option})");
                await Page.Locator("#GenerateWalletLink").ClickAsync();
                await Page.Locator($"#Generate{option}Link").ClickAsync();
            }

            await Page.Locator("#ScriptPubKeyType").ClickAsync();
            await Page.Locator($"#ScriptPubKeyType option[value={format}]").ClickAsync();
            await Page.Locator("[data-toggle='collapse'][href='#AdvancedSettings']").ClickAsync();
            if (importkeys is bool v)
                await Page.Locator("#ImportKeysToRPC").SetCheckedAsync(v);
            await Page.Locator("#Continue").ClickAsync();

            if (isImport)
            {
                // Confirm addresses
                await Page.Locator("#Confirm").ClickAsync();
            }
            else
            {
                // Seed backup
                await FindAlertMessage();
                if (string.IsNullOrEmpty(seed))
                {
                    seed = await Page.Locator("#RecoveryPhrase").First.GetAttributeAsync("data-mnemonic");
                }

                // Confirm seed backup
                await Page.Locator("#confirm").ClickAsync();
                await Page.Locator("#submit").ClickAsync();
            }

            WalletId = new WalletId(StoreId, cryptoCode);
            return new Mnemonic(seed);
        }

        public async Task GoToRegister()
        {
            await GoToUrl("/register");
        }
        public async Task GoToLogin()
        {
            await GoToUrl("/login");
        }
        public async Task Logout()
        {
            await Page.Locator("#Nav-Account").ClickAsync();
            await Page.Locator("#Nav-Logout").ClickAsync();
        }

        public async Task GoToHome()
        {
            var skipWizard = Page.Locator("#SkipWizard");
            if (await skipWizard.IsVisibleAsync())
            {
                await skipWizard.ClickAsync();
            }
            else { await GoToUrl("/"); }
        }
        public async Task LogIn(string user, string password = "123456")
        {
            await Page.Locator("#Email").FillAsync(user);
            await Page.Locator("#Password").FillAsync(password);
            await Page.Locator("#LoginButton").ClickAsync();
        }

        public async Task GoToProfile(ManageNavPages navPages = ManageNavPages.Index)
        {
            await Page.Locator("#Nav-Account").ClickAsync();
            await Page.Locator("#Nav-ManageAccount").ClickAsync();
            if (navPages != ManageNavPages.Index)
            {
                await Page.Locator($"#SectionNav-{navPages.ToString()}").ClickAsync();
            }
        }

        public async Task GoToServer(ServerNavPages navPages = ServerNavPages.Policies)
        {
            await Page.Locator("#Nav-ServerSettings").ClickAsync();
            if (navPages != ServerNavPages.Policies)
            {
                await Page.Locator($"#SectionNav-{navPages}").ClickAsync();
            }
        }

        public async Task ClickOnAllSectionLinks()
        {
            var links = await Page.Locator("#SectionNav .nav-link").EvaluateAllAsync<string[]>("els => els.map(e => e.href)");

            foreach (var link in links)
            {
                TestLogs.LogInformation($"Checking no error on {link}");
                await Page.GotoAsync(link);
                await Page.AssertNoError();
            }
        }

        /// <summary>
        ///     Assume to be in store's settings
        /// </summary>
        /// <param name="cryptoCode"></param>
        /// <param name="derivationScheme"></param>
        public async Task AddDerivationScheme(string cryptoCode = "BTC",
            string derivationScheme = "tpubD6NzVbkrYhZ4XxNXjYTcRujMc8z8734diCthtFGgDMimbG5hUsKBuSTCuUyxWL7YwP7R4A5StMTRQiZnb6vE4pdHWPgy9hbiHuVJfBMumUu-[legacy]")
        {
            if (!(await Page.ContentAsync()).Contains($"Setup {cryptoCode} Wallet"))
                await GoToWalletSettings(cryptoCode);

            await Page.Locator("#ImportWalletOptionsLink").ClickAsync();
            await Page.Locator("#ImportXpubLink").ClickAsync();
            await Page.FillAsync("#DerivationScheme", derivationScheme);
            await Page.Locator("#Continue").ClickAsync();
            await Page.Locator("#Confirm").ClickAsync();
            await FindAlertMessage();
        }

        public async Task ClickPagePrimary()
        {
            await Page.Locator("#page-primary").ClickAsync();
        }

        public async Task GoToWalletSettings(string cryptoCode = "BTC")
        {
            await Page.ClickAsync($"#StoreNav-Wallet{cryptoCode}");
            var walletNavSettings = Page.Locator("#WalletNav-Settings");
            if (await walletNavSettings.CountAsync() > 0)
                await walletNavSettings.ClickAsync();
        }

        public async Task GoToStore(StoreNavPages storeNavPage = StoreNavPages.General)
        {
            await GoToStore(null, storeNavPage);
        }

        public async Task GoToStore(string storeId, StoreNavPages storeNavPage = StoreNavPages.General)
        {
            if (storeId is not null)
            {
                await GoToUrl($"/stores/{storeId}/");
                StoreId = storeId;
                if (WalletId != null)
                    WalletId = new WalletId(storeId, WalletId.CryptoCode);
            }
            await Page.Locator($"#StoreNav-{storeNavPage}").ClickAsync();
        }
        public async Task ClickCancel()
        {
            await Page.Locator("#CancelWizard").ClickAsync();
        }


        public async Task InitializeBTCPayServer()
        {
            await RegisterNewUser(true);
            await CreateNewStore();
            await GoToStore();
            await AddDerivationScheme();
        }


        public void Dispose()
        {
            static void Try(Action action)
            {
                try
                { action(); }
                catch { }
            }

            Try(() =>
            {
                Page?.CloseAsync().GetAwaiter().GetResult();
                Page = null;
            });

            Try(() =>
            {
                Browser?.CloseAsync().GetAwaiter().GetResult();
                Browser = null;
            });
            Server?.Dispose();
        }
    }
}
