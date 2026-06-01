using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Security;

public class BTCPayAuthenticatorTokenProvider : AuthenticatorTokenProvider<ApplicationUser>
{
    public override async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
        => user.AuthenticatorEnabled && await base.CanGenerateTwoFactorTokenAsync(manager, user);
}
