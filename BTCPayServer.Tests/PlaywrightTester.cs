using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using BTCPayServer.Views.Wallets;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using NBitcoin;
using NBitcoin.RPC;
using Xunit;

namespace BTCPayServer.Tests
{
    public class PlaywrightTester : IAsyncDisposable
    {
        public Uri ServerUri;
        public string CreatedUser;
        internal string InvoiceId;
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
            var conf = builder.Build();
            var playwright = await Playwright.CreateAsync();
            Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = Server.PayTester.InContainer || conf["PLAYWRIGHT_HEADLESS"] == "true",
                ExecutablePath = conf["PLAYWRIGHT_EXECUTABLE"],
                SlowMo = 0, // 50 if you want to slow down
                Args = ["--disable-frame-rate-limit"] // Fix slowness on linux (https://github.com/microsoft/playwright/issues/34625#issuecomment-2822015672)
            });
            var context = await Browser.NewContextAsync();
            Page = await context.NewPageAsync();
            ServerUri = Server.PayTester.ServerUri;
            TestLogs.LogInformation($"Playwright: Using {Page.GetType()}");
            TestLogs.LogInformation($"Playwright: Browsing to {ServerUri}");
            await GoToRegister();
            await Page.AssertNoError();
        }

        public async Task GoToInvoices(string storeId = null)
        {
            if (storeId is null)
            {
                await Page.Locator("#menu-item-Invoices").ClickAsync();
            }
            else
            {
                await GoToUrl($"/stores/{storeId}/invoices/");
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

            var statusText = await (await FindAlertMessage(expectedSeverity)).TextContentAsync();
            var inv = expectedSeverity == StatusMessageModel.StatusSeverity.Success
                ? Regex.Match(statusText!, @"Invoice (\w+) just created!").Groups[1].Value
                : null;

            InvoiceId = inv;
            TestLogs.LogInformation($"Created invoice {inv}");
            return inv;
        }

        public async Task GoToInvoiceCheckout(string invoiceId = null)
        {
            invoiceId ??= InvoiceId;
            await GoToUrl($"/i/{invoiceId}");
        }

        public async Task GoToWallet(WalletId walletId = null, WalletsNavPages? navPages = null)
        {
            var walletPage = GetWalletIdFromUrl();
            // If null, we try to go to the wallet of the current page
            if (walletId is null)
            {
                if (walletPage is not null)
                    walletId = WalletId.Parse(walletPage);
                walletId ??= WalletId;
                if (walletId is not null && walletId.ToString() != walletPage)
                    await GoToUrl($"wallets/{walletId}");
            }
            // If not, we browse to the wallet before going to subpages
            else
            {
                await GoToUrl($"wallets/{walletId}");
            }

            var cryptoCode = walletId?.CryptoCode ?? "BTC";
            if (navPages is null)
            {
                await Page.GetByTestId("Wallet-" + cryptoCode).Locator("a").ClickAsync();
            }
            else if (navPages == WalletsNavPages.PSBT)
            {
                await Page.Locator($"#menu-item-Send-{cryptoCode}").ClickAsync();
                await Page.Locator("#PSBT").ClickAsync();
            }
            else
            {
                await Page.Locator($"#menu-item-{navPages}-{cryptoCode}").ClickAsync();
            }
        }

        private string GetWalletIdFromUrl()
        {
            var m = Regex.Match(Page.Url, "wallets/([^/]+)");
            if (m.Success)
                return m.Groups[1].Value;

            m = Regex.Match(Page.Url, "([^/]+)/onchain/([^/]+)");
            if (m.Success)
                return new WalletId(m.Groups[1].Value, m.Groups[2].Value).ToString();
            return null;
        }

        public async Task<ILocator> FindAlertMessage(StatusMessageModel.StatusSeverity severity = StatusMessageModel.StatusSeverity.Success,
            string partialText = null)
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

        public async Task GoToUrl(string uri)
        {
            await Page.GotoAsync(Link(uri), new() { WaitUntil = WaitUntilState.Commit });
        }

        public string Link(string uri)
        {
            if (Uri.IsWellFormedUriString(uri, UriKind.Absolute))
                return uri;
            return ServerUri.AbsoluteUri.WithoutEndingSlash() + uri.WithStartingSlash();
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
            return new TestAccount(Server)
            {
                StoreId = StoreId, Email = CreatedUser, Password = Password,
                RegisterDetails = new Models.AccountViewModels.RegisterViewModel() { Password = "123456", Email = CreatedUser }, IsAdmin = IsAdmin
            };
        }

        public async Task<(string storeName, string storeId)> CreateNewStore(bool keepId = true, string preferredExchange = "CoinGecko")
        {
            if (!Page.Url.EndsWith("stores/create"))
            {
                if (await Page.Locator("#StoreSelectorToggle").IsVisibleAsync())
                {
                    await Page.ClickAsync("#StoreSelectorToggle");
                    await Page.ClickAsync("#StoreSelectorCreate");
                }
                else
                {
                    await GoToUrl("/stores/create");
                }
            }

            var name = "Store" + RandomUtils.GetUInt64();
            TestLogs.LogInformation($"Created store {name}");
            await Page.FillAsync("#Name", name);

            var selectedOption = await Page.Locator("#PreferredExchange option:checked").TextContentAsync();
            Assert.Equal("Recommendation (Kraken)", selectedOption?.Trim());
            await Page.Locator("#PreferredExchange").SelectOptionAsync(new SelectOptionValue { Label = preferredExchange });
            await Page.ClickAsync("#Create");
            await GoToStore(StoreNavPages.General);
            var storeId = await Page.InputValueAsync("#Id");
            if (keepId)
                StoreId = storeId;

            return (name, storeId);
        }

        public async Task<Mnemonic> GenerateWallet(string cryptoCode = "BTC", string seed = "", bool? importkeys = null, bool isHotWallet = false,
            ScriptPubKeyType format = ScriptPubKeyType.Segwit)
        {
            var isImport = !string.IsNullOrEmpty(seed);
            await GoToWalletSettings(cryptoCode);
            // Replace previous wallet case
            var isSettings = Page.Url.EndsWith("/settings");
            if (isSettings)
            {
                TestLogs.LogInformation($"Replacing the wallet");
                await Page.ClickAsync("#ActionsDropdownToggle");
                await Page.ClickAsync("#ChangeWalletLink");
                await Page.FillAsync("#ConfirmInput", "REPLACE");
                await Page.ClickAsync("#ConfirmContinue");
            }

            if (isImport)
            {
                TestLogs.LogInformation("Progressing with existing seed");
                await Page.ClickAsync("#ImportWalletOptionsLink");
                await Page.ClickAsync("#ImportSeedLink");
                await Page.FillAsync("#ExistingMnemonic", seed);
                await Page.Locator("#SavePrivateKeys").SetCheckedAsync(isHotWallet);
            }
            else
            {
                var option = isHotWallet ? "Hotwallet" : "Watchonly";
                TestLogs.LogInformation($"Generating new seed ({option})");
                await Page.ClickAsync("#GenerateWalletLink");
                await Page.ClickAsync($"#Generate{option}Link");
            }

            await Page.SelectOptionAsync("#ScriptPubKeyType", new SelectOptionValue { Value = format.ToString() });
            await Page.ClickAsync("#AdvancedSettingsButton");
            if (importkeys is bool v)
                await Page.Locator("#ImportKeysToRPC").SetCheckedAsync(v);
            await Page.ClickAsync("#Continue");

            if (isImport)
            {
                // Confirm addresses
                await Page.ClickAsync("#Confirm");
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
                await Page.ClickAsync("#confirm");
                await Page.ClickAsync("#submit");
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
            await Page.Locator("#menu-item-Account").ClickAsync();
            await Page.Locator("#Nav-Logout").ClickAsync();
        }

        public Task SkipWizard() => Page.ClickAsync("#SkipWizard");

        public async Task GoToHome()
        {
            var skipWizard = Page.Locator("#SkipWizard");
            if (await skipWizard.IsVisibleAsync())
            {
                await skipWizard.ClickAsync();
            }
            else { await GoToUrl("/"); }
        }

        public async Task AddUserToStore(string storeId, string email, string role)
        {
            var addUser = Page.Locator("#AddUser");
            if (!await addUser.IsVisibleAsync())
            {
                await GoToStore(storeId, StoreNavPages.Users);
            }

            await Page.FillAsync("#Email", email);
            await Page.SelectOptionAsync("#Role", role);
            await Page.ClickAsync("#AddUser");
            await FindAlertMessage(partialText: "The user has been added successfully");
        }

        public async Task LogIn(string user, string password = "123456")
        {
            await Page.FillAsync("#Email", user);
            await Page.FillAsync("#Password", password);
            await Page.ClickAsync("#LoginButton");
        }

        public Task GoToProfile(ManageNavPages navPages = ManageNavPages.Index)
            => GoToProfile(navPages.ToString());

        public async Task GoToProfile(string navPages)
        {
            await Page.ClickAsync("#menu-item-Account");
            await Page.ClickAsync("#Nav-ManageAccount");
            if (navPages != nameof(ManageNavPages.Index))
            {
                await Page.ClickAsync($"#menu-item-{navPages}");
            }
        }

        public Task GoToServer(ServerNavPages navPages = ServerNavPages.Policies)
            => GoToServer(navPages.ToString());

        public async Task GoToServer(string navPages)
        {
            await Page.ClickAsync("#menu-item-Policies");
            if (navPages == nameof(ServerNavPages.Emails))
            {
                await Page.ClickAsync($"#menu-item-Server-{navPages}");
            }
            else if (navPages != nameof(ServerNavPages.Policies))
            {
                await Page.ClickAsync($"#menu-item-{navPages}");
            }
        }

        public async Task ClickOnAllSectionLinks(string sectionSelector = "#menu-item")
        {
            List<string> links = [];
            foreach (var locator in await Page.Locator($"{sectionSelector} .nav-link").AllAsync())
            {
                var link = await locator.GetAttributeAsync("href");
                if (link is null or "/logout")
                    continue;
                Assert.NotNull(link);
                links.Add(link);
            }

            Assert.NotEmpty(links);
            foreach (var link in links)
            {
                TestLogs.LogInformation($"Checking no error on {link}");
                await GoToUrl(link);
                await Page.AssertNoError();
            }
        }

        /// <summary>
        ///     Assume to be in store's settings
        /// </summary>
        /// <param name="cryptoCode"></param>
        /// <param name="derivationScheme"></param>
        public async Task AddDerivationScheme(string cryptoCode = "BTC",
            string derivationScheme =
                "tpubD6NzVbkrYhZ4XxNXjYTcRujMc8z8734diCthtFGgDMimbG5hUsKBuSTCuUyxWL7YwP7R4A5StMTRQiZnb6vE4pdHWPgy9hbiHuVJfBMumUu-[legacy]")
        {
            if (cryptoCode != "BTC" && derivationScheme ==
                "tpubD6NzVbkrYhZ4XxNXjYTcRujMc8z8734diCthtFGgDMimbG5hUsKBuSTCuUyxWL7YwP7R4A5StMTRQiZnb6vE4pdHWPgy9hbiHuVJfBMumUu-[legacy]")
            {
                derivationScheme =
                    new BitcoinExtPubKey("tpubD6NzVbkrYhZ4XxNXjYTcRujMc8z8734diCthtFGgDMimbG5hUsKBuSTCuUyxWL7YwP7R4A5StMTRQiZnb6vE4pdHWPgy9hbiHuVJfBMumUu",
                            Network.RegTest)
                        .ToNetwork(NBitcoin.Altcoins.Litecoin.Instance.Regtest)
                        .ToString()! + "-[legacy]";
            }

            if (!(await Page.ContentAsync()).Contains($"Setup {cryptoCode} Wallet"))
                await GoToWalletSettings(cryptoCode);

            await Page.Locator("#ImportWalletOptionsLink").ClickAsync();
            await Page.Locator("#ImportXpubLink").ClickAsync();
            await Page.FillAsync("#DerivationScheme", derivationScheme);
            await Page.Locator("#Continue").ClickAsync();
            await Page.Locator("#Confirm").ClickAsync();
            await FindAlertMessage();
        }

        public async Task AddLightningNode(string connectionType = null, bool test = true)
        {
            var cryptoCode = "BTC";
            if (!(await Page.ContentAsync()).Contains("Connect to a Lightning node"))
            {
                await GoToLightningSettings();
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
                Assert.True(await Page.IsEnabledAsync("#LightningNodeType-Internal"), "Usage of the internal Lightning node is disabled.");
                await Page.ClickAsync("label[for=\"LightningNodeType-Internal\"]");
            }
            else
            {
                await Page.ClickAsync("label[for=\"LightningNodeType-Custom\"]");
                await Page.FillAsync("#ConnectionString", connectionString);
                if (test)
                {
                    await Page.ClickAsync("#test");
                    await FindAlertMessage(partialText: "Connection to the Lightning node successful.");
                }
            }

            await ClickPagePrimary();
            await FindAlertMessage(partialText: $"{cryptoCode} Lightning node updated.");

            var enabled = await Page.WaitForSelectorAsync($"#{cryptoCode}LightningEnabled");
            if (!await enabled!.IsCheckedAsync())
            {
                await enabled.ClickAsync();
                await ClickPagePrimary();
                await FindAlertMessage(partialText: $"{cryptoCode} Lightning settings successfully updated");
            }
        }

        public async Task GoToLightningSettings(string cryptoCode = "BTC")
        {
            await Page.GetByTestId("Lightning-" + cryptoCode).ClickAsync();
            // if Lightning is already set up we need to navigate to the settings
            if ((await Page.ContentAsync()).Contains($"id=\"menu-item-LightningSettings-{cryptoCode}\""))
            {
                await Page.ClickAsync($"#menu-item-LightningSettings-{cryptoCode}");
            }
        }

        public async Task ClickPagePrimary()
        {
            await Page.Locator("#page-primary").ClickAsync();
        }

        public async Task GoToWalletSettings(string cryptoCode = "BTC")
        {
            await Page.GetByTestId("Wallet-" + cryptoCode).Locator("a").ClickAsync();
            var walletNavSettings = Page.Locator($"#menu-item-Settings-{cryptoCode}");
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
                if (storeNavPage != StoreNavPages.General)
                    await Page.Locator($"#menu-item-{StoreNavPages.General}").ClickAsync();
            }

            await Page.Locator($"#menu-item-{storeNavPage}").ClickAsync();
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


        public async ValueTask DisposeAsync()
        {
            static async Task Try(Func<Task> action)
            {
                try
                {
                    await action();
                }
                catch { }
            }

            await Try(async () =>
            {
                if (Page is null)
                    return;
                await Page.CloseAsync();
                Page = null;
            });

            await Try(async () =>
            {
                if (Browser is null)
                    return;
                await Browser.CloseAsync();
                Browser = null;
            });
            Server?.Dispose();
        }

        public async Task<string> FundStoreWallet(WalletId walletId = null, int coins = 1, decimal denomination = 1m)
        {
            walletId ??= WalletId;
            await GoToWallet(walletId, WalletsNavPages.Receive);
            var addressStr = await Page.Locator("#Address").GetAttributeAsync("data-text");
            var address = BitcoinAddress.Create(addressStr!, ((BTCPayNetwork)Server.NetworkProvider.GetNetwork(walletId.CryptoCode)).NBitcoinNetwork);
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

            await Server.ExplorerNode.GenerateAsync(1);
            await Page.ReloadAsync();
            await Page.Locator("#CancelWizard").ClickAsync();
            return addressStr;
        }

        public Task GoToInvoice(string invoiceId) => GoToUrl($"/invoices/{invoiceId}/");

        public async Task ConfirmModal()
        {
            await Page.ClickAsync(".modal.fade.show .modal-confirm");
        }

        public async Task<(string appName, string appId)> CreateApp(string type, string name = null)
        {
            if (string.IsNullOrEmpty(name))
                name = $"{type}-{Guid.NewGuid().ToString()[..14]}";
            await Page.Locator($"#menu-item-CreateApp-{type}").ClickAsync();
            await Page.Locator("[name='AppName']").FillAsync(name);
            await ClickPagePrimary();
            await FindAlertMessage(partialText: "App successfully created");
            var appId = Page.Url.Split('/')[^3];
            return (name, appId);
        }

        public async Task PayInvoice(bool mine = false, decimal? amount = null, bool clickRedirect = false)
        {
            if (amount is not null)
            {
                await Page.FillAsync("#test-payment-amount", amount.ToString());
            }

            await Page.ClickAsync("#FakePayment");
            await Page.Locator("#CheatSuccessMessage").WaitForAsync();
            if (mine)
            {
                await MineBlockOnInvoiceCheckout();
            }

            if (amount is null)
                await Page.Locator("xpath=//*[text()=\"Invoice Paid\" or text()=\"Payment Received\"]").WaitForAsync();
            else
                await Page.Locator("xpath=//*[text()=\"Invoice Paid\" or text()=\"Payment Received\" or text()=\"The invoice hasn't been paid in full.\"]")
                    .WaitForAsync();
            if (clickRedirect)
            {
                await Page.ClickAsync("#StoreLink");
            }
        }

        /// <summary>
        /// Take a screenshot. If running in CI, it is uploaded in the artifacts (see https://github.com/btcpayserver/btcpayserver/pull/6794)
        /// </summary>
        /// <param name="fileName"></param>
        public async Task TakeScreenshot(string fileName)
        {
            var screenshotDir = Environment.GetEnvironmentVariable("TESTS_ARTIFACTS_DIR") ?? "Screenshots";
            Directory.CreateDirectory(screenshotDir);
            screenshotDir = Path.Combine(screenshotDir, this.Server.Scope);
            Directory.CreateDirectory(screenshotDir);
            var filePath = Path.Combine(screenshotDir, fileName);
            Server.TestLogs.LogInformation("Saving test screenshot to " + Path.GetFullPath(filePath));
            await Page.ScreenshotAsync(new()
            {
                Path = filePath,
                FullPage = true,
            });
        }

        public async Task MineBlockOnInvoiceCheckout()
        {
            await Page.ClickAsync("#mine-block button");
        }

        class SwitchDisposable(IPage newPage, IPage oldPage, PlaywrightTester tester, bool closeAfter) : IAsyncDisposable
        {
            public async ValueTask DisposeAsync()
            {
                if (closeAfter)
                    await newPage.CloseAsync();
                tester.Page = oldPage;
            }
        }

        public async Task<IAsyncDisposable> SwitchPage(Task<IPage> page, bool closeAfter = true)
        {
            var p = await page;
            return await SwitchPage(p, closeAfter);
        }

        public async Task<IAsyncDisposable> SwitchPage(IPage page, bool closeAfter = true)
        {
            var old = Page;
            Page = page;
            await page.BringToFrontAsync();
            return new SwitchDisposable(page, old, this, closeAfter);
        }

        public async Task<IAsyncDisposable> SwitchPage()
        {
            var newPage = await Browser.NewPageAsync();
            return await SwitchPage(newPage);
        }

        public async Task<WalletTransactionsPMO> GoToWalletTransactions(WalletId walletId = null)
        {
            await GoToWallet(walletId, navPages: WalletsNavPages.Transactions);
            await Page.Locator("#WalletTransactions[data-loaded='true']").WaitForAsync(new() { State = WaitForSelectorState.Visible });
            return new WalletTransactionsPMO(this);
        }

#nullable enable
        public class WalletTransactionsPMO(PlaywrightTester tester)
        {
            private IPage Page => tester.Page;
            public Task SelectAll() => Page.SetCheckedAsync(".mass-action-select-all", true);

            public async Task Select(params uint256[] txs)
            {
                foreach (var txId in txs)
                {
                    await Page.SetCheckedAsync($"{TxRowSelector(txId)} .mass-action-select", true);
                }
            }

            public Task BumpFeeSelected() => Page.ClickAsync("#BumpFee");

            public Task BumpFee(uint256? txId = null) => Page.ClickAsync($"{TxRowSelector(txId)} .bumpFee-btn");
            static string TxRowSelector(uint256? txId = null) => txId is null ? ".transaction-row:first-of-type" : $".transaction-row[data-value=\"{txId}\"]";

            public async Task AssertRowContains(uint256 txId, string expected)
            {
                var text = await Page.InnerTextAsync(TxRowSelector(txId));
                Assert.Contains(expected.NormalizeWhitespaces(), text.NormalizeWhitespaces());
            }

            public Task AssertHasLabels(string label) => AssertHasLabels(null, label);

            public async Task AssertHasLabels(uint256? txId, string label)
            {
                // This is complicated, the labels are asynchronously added.
                // So we may need to refresh the page
                int tried = 0;
                retry:
                await WaitTransactionsLoaded();
                var selector = $"{TxRowSelector(txId)} .transaction-label[data-value=\"{label}\"]";
                if (await Page.Locator(selector).IsVisibleAsync())
                    return;
                if (tried > 5)
                {
                    try
                    {
                        await Page.Locator(selector).WaitForAsync();
                    }
                    catch
                    {
                        await tester.TakeScreenshot("AssertHasLabels.png");
                        throw;
                    }

                    return;
                }

                tried++;
                await Page.ReloadAsync();
                goto retry;
            }

            public Task WaitTransactionsLoaded() => Page.Locator("#WalletTransactions[data-loaded='true']").WaitForAsync();

            public async Task AssertNotFound(uint256 txId)
            {
                Assert.False(await Page.Locator(TxRowSelector(txId)).IsVisibleAsync());
            }
        }


        public async Task<SendWalletPMO> GoToWalletSend(WalletId? walletId = null)
        {
            await GoToWallet(walletId, navPages: WalletsNavPages.Send);
            return new(Page);
        }

        public class SendWalletPMO(IPage page)
        {
            public Task FillAddress(BitcoinAddress address) => page.FillAsync("[name='Outputs[0].DestinationAddress']",
                address.ToString());

            public Task SweepBalance() => page.ClickAsync("#SweepBalance");

            public Task Sign() => page.ClickAsync("#SignTransaction");

            public Task SetFeeRate(decimal val) => page.FillAsync("[name=\"FeeSatoshiPerByte\"]", val.ToString(CultureInfo.InvariantCulture));

            public Task FillAmount(decimal amount) => page.FillAsync("[name='Outputs[0].Amount']", amount.ToString(CultureInfo.InvariantCulture));
        }

        public async Task MarkAsSettled()
        {
            var txId = Regex.Replace(Page.Url, ".*/(.*)$", "$1");
            var client = await this.AsTestAccount().CreateClient();
            await client.MarkInvoiceStatus(StoreId, txId, new() { Status = InvoiceStatus.Settled });
        }

        public WalletTransactionsPMO InWalletTransactions() => new WalletTransactionsPMO(this);

        public WalletBroadcastPMO InBroadcast() => new WalletBroadcastPMO(Page);

        public class WalletBroadcastPMO(IPage page)
        {
            public async Task AssertSending(BitcoinAddress destination, decimal amount)
            {
                await page.WaitForSelectorAsync($"td:text('{destination}')");
                var amt = await page.WaitForSelectorAsync($"td:text('{destination}') >> .. >> :nth-child(3)");
                var actual = (await amt!.TextContentAsync()).NormalizeWhitespaces();
                var expected = ("-" + Money.Coins(amount).ToString() + " " + "BTC").NormalizeWhitespaces();
                Assert.Equal(expected, actual);
            }

            public async Task Broadcast() => await page.ClickAsync("#BroadcastTransaction");
        }

        public async Task ClickViewReport()
        {
            await Page.ClickAsync("#view-report");
            await Page.WaitForSelectorAsync("#raw-data-table");
        }

        public async Task<string> DownloadReportCSV()
        {
            var download = await Page.RunAndWaitForDownloadAsync(async () =>
            {
                await ClickPagePrimary();
            });
            return await new StreamReader(await download.CreateReadStreamAsync()).ReadToEndAsync();
        }

        public async Task ConfirmDeleteModal()
        {
            await Page.FillAsync("#ConfirmInput", "DELETE");
            await Page.ClickAsync("#ConfirmContinue");
        }

        public async Task AssertPageAccess(bool shouldHaveAccess, string url)
        {
            await GoToUrl(url);
            await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var content = await Page.ContentAsync();
            Assert.DoesNotContain("404 - Page not found", content);
            if (shouldHaveAccess)
            {
                Assert.DoesNotContain("- Denied</h", content);
                // check associated link is active if present
                var hrefToMatch = new Uri(Link(url), UriKind.Absolute).AbsolutePath.TrimEnd('/');
                var sidebarLink = Page.Locator($"#mainNav a[href=\"{hrefToMatch}\"], #mainNav a[href=\"{hrefToMatch}/\"]");
                if (await sidebarLink.CountAsync() > 0)
                {
                    var classAttr = await sidebarLink.First.GetAttributeAsync("class");
                    Assert.Contains("active", classAttr);
                }
            }
            else
            {
                Assert.Contains("- Denied</h", content);
            }
        }

        public Task FastReloadAsync()
            => Page.ReloadAsync(new() { WaitUntil = WaitUntilState.Commit });

        public async Task ClickOnEmailLink(MailPitClient.Message email)
        {
            var match = Regex.Match(email.Html, "href=[\"']([^\"']+)[\"']");
            if (!match.Success)
                throw new InvalidOperationException("No href found in email HTML");
            var link = match.Groups[1].Value;
            link = System.Net.WebUtility.HtmlDecode(link);
            await GoToUrl(link);
        }
    }
}
