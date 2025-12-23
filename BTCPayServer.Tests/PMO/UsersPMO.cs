using System.Threading.Tasks;

namespace BTCPayServer.Tests.PMO;

public class UsersPMO(PlaywrightTester s)
{
    public async Task DeleteUser(string email)
    {
        await s.Page.ClickAsync($"tr[data-email=\"{email}\"] .delete-user");
        await s.Page.ClickAsync(".modal-confirm");
        await s.FindAlertMessage();
    }
}
