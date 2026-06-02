using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Fido2;

public class Fido2TokenProvider(ApplicationDbContextFactory dbContextFactory) : IUserTwoFactorTokenProvider<ApplicationUser>
{
    public Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
        => Task.FromResult(string.Empty);

    public Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
        => Task.FromResult(false);

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        // Only FIDO2 and LNURLAuth credentials can generate two-factor tokens (Passkey is not second factor)
        await using var context = dbContextFactory.CreateContext();
        return await context.Fido2Credentials.AnyAsync(f => f.ApplicationUserId == user.Id && (f.Type == Fido2Credential.CredentialType.FIDO2 || f.Type == Fido2Credential.CredentialType.LNURLAuth));
    }
}
