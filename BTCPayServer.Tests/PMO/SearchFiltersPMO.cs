using System.Threading.Tasks;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests.PMO;

public class SearchFiltersPMO(PlaywrightTester s)
{
    public ILocator SearchText => s.Page.Locator(".search-string-input__text");
    public ILocator SearchTerm => s.Page.Locator(".search-string-input__term");
    public ILocator ClearAllFiltersButton => s.Page.Locator(".clear-all-filters__button");
    public ILocator DateRangeSelector => s.Page.Locator(".date-range-selector__toggle");
    public ILocator DateRangeTimeZone => s.Page.Locator(".date-range-selector__timezone");
    public ILocator LabelSelectorToggle => s.Page.Locator(".label-selector__toggle");

    public async Task FillSearchText(string searchText)
    {
        await SearchText.FillAsync(searchText);
        await SearchText.PressAsync("Enter");
        await s.Page.WaitForLoadStateAsync();
    }

    public Task<string> SearchTextValue() => SearchText.InputValueAsync();
    public Task<string> SearchTermValue() => SearchTerm.InputValueAsync();

    public Task AssertSearchText(string searchText) =>
        Expect(SearchText).ToHaveValueAsync(searchText);

    public async Task ClearAllFilters()
    {
        await ClearAllFiltersButton.ClickAsync();
        await s.Page.WaitForLoadStateAsync();
    }

    public async Task SelectDateRangePreset(string preset)
    {
        await DateRangeSelector.ClickAsync();
        await s.Page.ClickAsync($".date-range-selector__preset:has-text('{preset}')");
    }

    public Task OpenDateRange() => DateRangeSelector.ClickAsync();

    public async Task SelectCustomStartDate(string startDate)
    {
        await OpenDateRange();
        await s.Page.ClickAsync(".date-range-selector__custom-range");
        await s.Page.Locator(".date-range-selector__custom-range-start[type='hidden']")
            .EvaluateAsync("(input, startDate) => { input.value = startDate; }", startDate);
        await s.Page.ClickAsync(".date-range-selector__custom-range-submit");
    }

    public async Task SelectTimeZone(string timeZone)
    {
        await DateRangeTimeZone.FillAsync(timeZone);
        await DateRangeTimeZone.PressAsync("Enter");
        await s.Page.WaitForLoadStateAsync();
    }

    public async Task SelectLabel(string label)
    {
        await s.Page.ClickAsync(".label-selector");
        await s.Page.ClickAsync($".label-selector__item span:has-text('{label}')");
    }

    public async Task SelectNoLabel()
    {
        await s.Page.ClickAsync(".label-selector");
        await s.Page.ClickAsync(".label-selector__no-label:text-is('No Label')");
    }
}
