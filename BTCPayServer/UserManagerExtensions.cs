#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer
{
    public static class UserManagerExtensions
    {
        private const string InvitationPurpose = "invitation";

        public static async Task<TUser?> FindByIdOrEmail<TUser>(this UserManager<TUser> userManager, string? idOrEmail) where TUser : class
        {
            if (string.IsNullOrEmpty(idOrEmail))
                return null;
            if (idOrEmail.Contains('@'))
                return await userManager.FindByEmailAsync(idOrEmail);

            return await userManager.FindByIdAsync(idOrEmail);
        }

        public static async Task<string> GenerateInvitationTokenAsync<TUser>(this UserManager<TUser> userManager, TUser user) where TUser : class
        {
            return await userManager.GenerateUserTokenAsync(user, InvitationTokenProviderOptions.ProviderName, InvitationPurpose);
        }

        public static async Task<TUser?> FindByInvitationTokenAsync<TUser>(this UserManager<TUser> userManager, string userId, string token) where TUser : class
        {
            var user = await userManager.FindByIdAsync(userId);
            var isValid = user is not null && await userManager.VerifyUserTokenAsync(user, InvitationTokenProviderOptions.ProviderName, InvitationPurpose, token);
            return isValid ? user : null;
        }
    }
}
