using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using Microsoft.Playwright;
using NBitcoin;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests;

public class PlaywrightBaseTest : UnitTestBase, IAsyncLifetime
{
    private string CreatedUser;
    private string InvoiceId;
    public PlaywrightBaseTest(ITestOutputHelper helper) : base(helper)
    {
    }

    public WalletId WalletId { get; set; }
    public IPlaywright Playwright { get; private set; }
    public IBrowser Browser { get; private set; }
    public IPage Page { get; private set; }
    public Uri ServerUri { get; set; }
    public string Password { get; private set; }
    public string StoreId { get; private set; }
    public bool IsAdmin { get; private set; }





    public async Task InitializeAsync()
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false, // Set to true for CI
            SlowMo = 50 // Add slight delay between actions to improve stability
        });
        var context = await Browser.NewContextAsync();
        Page = await context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await Page?.CloseAsync();
        await Browser?.CloseAsync();
        Playwright?.Dispose();
    }


    public async Task InitializeBTCPayServer()
    {
        await RegisterNewUser(true);
        await CreateNewStoreAsync();
        await GoToStore();
        await AddDerivationScheme();
    }


    public async Task GoToUrl(string relativeUrl)
    {
        await Page.GotoAsync(Link(relativeUrl));
    }

    public async Task GoToHome()
    {
        var skipWizard = Page.Locator("#SkipWizard");
        if (await skipWizard.IsVisibleAsync())
        {
            await skipWizard.ClickAsync();
        }
        else
        {
            await GoToUrl("/");
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


    public string Link(string relativeLink)
    {
        return ServerUri.AbsoluteUri.WithoutEndingSlash() + relativeLink.WithStartingSlash();
    }

    public async Task<string> RegisterNewUser(bool isAdmin = false)
    {
        await GoToUrl("/register");
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

    public async Task<(string storeName, string storeId)> CreateNewStoreAsync(bool keepId = true)
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

    /// <summary>
    ///     Assume to be in store's settings
    /// </summary>
    /// <param name="cryptoCode"></param>
    /// <param name="derivationScheme"></param>
    public async Task AddDerivationScheme(string cryptoCode = "BTC",
        string derivationScheme = "tpubD6NzVbkrYhZ4XxNXjYTcRujMc8z8734diCthtFGgDMimbG5hUsKBuSTCuUyxWL7YwP7R4A5StMTRQiZnb6vE4pdHWPgy9hbiHuVJfBMumUu-[legacy]")
    {
        if (!(await Page.ContentAsync()).Contains($"Setup {cryptoCode} Wallet"))
            await GoToWalletSettingsAsync(cryptoCode);

        await Page.Locator("#ImportWalletOptionsLink").ClickAsync();
        await Page.Locator("#ImportXpubLink").ClickAsync();
        await Page.FillAsync("#DerivationScheme", derivationScheme);
        await Page.Locator("#Continue").ClickAsync();
        await Page.Locator("#Confirm").ClickAsync();
        await FindAlertMessageAsync();
    }

    public async Task<Mnemonic> GenerateWalletAsync(string cryptoCode = "BTC", string seed = "", bool? importkeys = null, bool isHotWallet = false, ScriptPubKeyType format = ScriptPubKeyType.Segwit)
    {
        var isImport = !string.IsNullOrEmpty(seed);
        await GoToWalletSettingsAsync(cryptoCode);
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
            await FindAlertMessageAsync();
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

    public async Task PayInvoiceAsync(IPage page, bool mine = false, decimal? amount = null)
    {
        if (amount is not null)
        {
            try
            {
                await page.Locator("#test-payment-amount").ClearAsync();
            }
            // Sometimes the element is not available after a window switch... retry
            catch (StaleElementReferenceException)
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
            await MineBlockOnInvoiceCheckoutAsync(page);
        }
    }

    public async Task MineBlockOnInvoiceCheckoutAsync(IPage page)
    {
retry:
        try
        {
            await page.Locator("#mine-block button").ClickAsync();
        }
        catch (PlaywrightException)
        {
            goto retry;
        }
    }


    public async Task GoToWalletSettingsAsync(string cryptoCode = "BTC")
    {
        await Page.ClickAsync($"#StoreNav-Wallet{cryptoCode}");
        var walletNavSettings = Page.Locator("#WalletNav-Settings");
        if (await walletNavSettings.CountAsync() > 0)
            await walletNavSettings.ClickAsync();
    }

    public async Task<ILocator> FindAlertMessageAsync(StatusMessageModel.StatusSeverity severity = StatusMessageModel.StatusSeverity.Success)
    {
        return await FindAlertMessageAsync(new[] { severity });
    }

    public async Task<ILocator> FindAlertMessageAsync(params StatusMessageModel.StatusSeverity[] severity)
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

    public async Task<ILocator> FindAlertMessageAsync(StatusMessageModel.StatusSeverity[] severity, IPage page)
    {
        var className = string.Join(", ", severity.Select(statusSeverity => $".alert-{StatusMessageModel.ToString(statusSeverity)}"));
        var locator = page.Locator(className);
        try
        {
            await locator.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            var visibleElements = await locator.AllAsync();
            var visibleElement = visibleElements.FirstOrDefault(el => el.IsVisibleAsync().GetAwaiter().GetResult());
            if (visibleElement != null)
                return page.Locator(className).First;

            return locator.First;
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Unable to find {className}");
        }
    }
    public async Task ClickPagePrimaryAsync()
    {
        try
        {
            await Page.Locator("#page-primary").ClickAsync();        
        }
        catch (NoSuchElementException)
        {
            await Page.Locator("#page-primary").ClickAsync();
        }
    }
}
