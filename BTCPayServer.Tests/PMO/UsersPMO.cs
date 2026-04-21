using System.Threading.Tasks;

namespace BTCPayServer.Tests.PMO;

public class UsersPMO(PlaywrightTester s)
{
    public async Task DeleteUser(string email)
    {
        await s.Page.ClickAsync($"{Row(email)} .delete-user");
        await s.Page.ClickAsync(".modal-confirm");
        await s.FindAlertMessage();
    }

    private static string Row(string email) => $"tr[data-email=\"{email}\"]";

    public async Task LogAs(string email)
    {
        await s.Page.ClickAsync($"{Row(email)} .user-impersonate");
        await s.Page.ClickAsync(".btn-primary");
    }
}
