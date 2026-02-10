using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Security;

public class DisabledEmailTokenProvider : EmailTokenProvider<ApplicationUser>
{
    public override Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        // We currently don't allow the user of email as two-factor authentication,
        // so we don't need to generate a token
        return Task.FromResult(false);
    }
}
