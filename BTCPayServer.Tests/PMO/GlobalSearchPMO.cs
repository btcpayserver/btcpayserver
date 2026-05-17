using System.Threading.Tasks;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests.PMO;

public class GlobalSearchPMO(PlaywrightTester tester)
{
    IPage Page => tester.Page;
    /// <summary>
    /// To use to nagivate to a static search item (such as a page quickly)
    /// </summary>
    /// <param name="page"></param>
    public async Task GoToPage(string page)
    {
        await Page.Keyboard.PressAsync("/");
        await Page.Keyboard.TypeAsync(page);
        await Page.Keyboard.PressAsync("Enter");
    }

    public async Task Fill(string query)
    {
        await Page.Keyboard.PressAsync("/");
        await Page.Locator("#globalSearchInput").FillAsync(query);
        await Page.Locator("#globalSearchResults:not([hidden])").WaitForAsync();
    }

    public Task Enter() => Page.Keyboard.PressAsync("Enter");


    public ILocator GetResultLocator(string partialText)
        => Page.Locator("#globalSearchResults .globalSearch-item", new() { HasTextString = partialText });

    public async Task<ILocator> AssertShow(string partialText)
    {
        var locator = GetResultLocator(partialText);
        await Expect(GetResultLocator(partialText)).ToBeVisibleAsync();
        return locator;
    }
}
