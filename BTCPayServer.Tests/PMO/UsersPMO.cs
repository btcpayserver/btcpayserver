using System.Threading.Tasks;
using static Microsoft.Playwright.Assertions;

namespace BTCPayServer.Tests.PMO;

public class UsersPMO(PlaywrightTester s)
{
    public async Task DeleteUser(string email)
    {
        await s.Page.ClickAsync($"{Row(email)} .delete-user");
        await s.Page.ClickAsync(".modal-confirm");
        await s.FindAlertMessage();
    }

    public async Task EditUser(string email)
    {
        await s.Page.ClickAsync($"{Row(email)} .user-edit");
    }

    private static string Row(string email) => $"tr[data-email=\"{email}\"]";

    public async Task AssertActive(string email)
    {
        await Expect(s.Page.Locator(Row(email) + " .user-status")).ToHaveTextAsync("Active");
    }
}
