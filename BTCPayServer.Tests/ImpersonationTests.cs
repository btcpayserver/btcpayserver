using System.Threading.Tasks;
using BTCPayServer.Views.Manage;
using BTCPayServer.Views.Server;
using BTCPayServer.Views.Stores;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests;

public class ImpersonationTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task CanSigninWithLoginCode()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        var user = await s.RegisterNewUser();
        await s.GoToHome();
        await s.GoToProfile(ManageNavPages.LoginCodes);

        await s.Page.WaitForSelectorAsync("#LoginCode .qr-code");
        var code = await s.Page.Locator("#LoginCode .qr-code").GetAttributeAsync("alt");
        string prevCode = code;
        await s.Page.ReloadAsync();
        await s.Page.WaitForSelectorAsync("#LoginCode .qr-code");
        code = await s.Page.Locator("#LoginCode .qr-code").GetAttributeAsync("alt");
        Assert.NotEqual(prevCode, code);
        await s.Logout();
        await s.GoToLogin();
        await s.Page.EvaluateAsync("document.getElementById('LoginCode').value = 'bad code'");
        await s.Page.EvaluateAsync("document.getElementById('logincode-form').submit()");
        await s.Page.WaitForLoadStateAsync();

        await s.GoToLogin();
        await s.Page.EvaluateAsync($"document.getElementById('LoginCode').value = '{code}'");
        await s.Page.EvaluateAsync("document.getElementById('logincode-form').submit()");
        await s.Page.WaitForLoadStateAsync();
        await s.Page.WaitForLoadStateAsync();

        await s.CreateNewStore();
        await s.GoToHome();
        await s.Page.WaitForLoadStateAsync();
        await s.Page.WaitForLoadStateAsync();
        var content = await s.Page.ContentAsync();
        Assert.Contains(user, content);
    }
}
