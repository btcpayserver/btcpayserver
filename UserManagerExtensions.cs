#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
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

        public static async Task<string?> GenerateInvitationTokenAsync(this UserManager<ApplicationUser> userManager, string userId)
        {
            var token = Guid.NewGuid().ToString("n")[..12];
            return await userManager.SetInvitationTokenAsync(userId, token) ? token : null;
        }

        public static Task<bool> UnsetInvitationTokenAsync(this UserManager<ApplicationUser> userManager, string userId)
        => userManager.SetInvitationTokenAsync(userId, null);

        public static bool HasInvitationToken(this UserManager<ApplicationUser> userManager, ApplicationUser user, string? token = null)
        {
            var blob = user.GetBlob() ?? new UserBlob();
            return token == null ? !string.IsNullOrEmpty(blob.InvitationToken) : blob.InvitationToken == token;
        }

        private static async Task<bool> SetInvitationTokenAsync(this UserManager<ApplicationUser> userManager, string userId, string? token)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null) return false;
            var blob = user.GetBlob() ?? new UserBlob();
            blob.InvitationToken = token;
            user.SetBlob(blob);
            await userManager.UpdateAsync(user);
            return true;
        }

        public static async Task<ApplicationUser?> FindByInvitationTokenAsync(this UserManager<ApplicationUser> userManager, string userId, string token)
        {
            var user = await userManager.FindByIdAsync(userId);
            var isValid = user is not null && (
                user.GetBlob()?.InvitationToken == token ||
                // backwards-compatibility with old tokens
                await userManager.VerifyUserTokenAsync(user, InvitationTokenProviderOptions.ProviderName, InvitationPurpose, token));
            return isValid ? user : null;
        }
    }
}
