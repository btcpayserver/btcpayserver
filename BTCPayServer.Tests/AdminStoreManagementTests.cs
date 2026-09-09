using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class AdminStoreManagementTests(ITestOutputHelper testOutputHelper) : UnitTestBase(testOutputHelper)
{
    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task AdminCanDeleteStoreLeftBehindByADeletedUser()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();

        var admin = await s.RegisterNewUser(true);
        await s.SkipWizard();
        await s.Logout();

        await s.GoToRegister();
        var owner = await s.RegisterNewUser();
        var (_, storeId) = await s.CreateNewStore();
        await s.Logout();

        await s.LogIn(admin);

        await s.GoToUrl($"/stores/{storeId}/settings");
        await Expect(s.Page.Locator("#DeleteStore")).ToHaveCountAsync(0);

        await s.GoToUrl("/server/users");
        await s.Page.Locator($"tr:has-text('{owner}')").Locator(".delete-user").ClickAsync();
        await s.Page.Locator("#ConfirmContinue").ClickAsync();
        await s.FindAlertMessage(partialText: "User deleted");

        await s.GoToUrl("/server/stores");
        await Expect(s.Page.Locator($"#store_{storeId}")).ToHaveCountAsync(1);
        await s.Page.ClickAsync($"#DeleteStore-{storeId}");
        await s.Page.Locator("#ConfirmContinue").ClickAsync();
        await s.FindAlertMessage(partialText: "has been deleted");
        await Expect(s.Page.Locator($"#store_{storeId}")).ToHaveCountAsync(0);
    }

    [Fact]
    [Trait("Playwright", "Playwright")]
    public async Task AdminCanArchiveAnotherUsersStore()
    {
        await using var s = CreatePlaywrightTester(newDb: true);
        await s.StartAsync();

        var admin = await s.RegisterNewUser(true);
        await s.SkipWizard();
        await s.Logout();
        await s.GoToRegister();
        await s.RegisterNewUser();
        var (_, storeId) = await s.CreateNewStore();
        await s.Logout();

        await s.LogIn(admin);
        await s.GoToUrl("/server/stores");

        var row = s.Page.Locator($"#store_{storeId}");
        await row.Locator(".store-list__archive").ClickAsync();
        await s.FindAlertMessage(partialText: "has been archived");
        await Expect(s.Page.Locator($"#store_{storeId}")).ToContainTextAsync("archived");

        await s.Page.Locator($"#store_{storeId}").Locator(".store-list__archive").ClickAsync();
        await s.FindAlertMessage(partialText: "has been unarchived");
    }
}
