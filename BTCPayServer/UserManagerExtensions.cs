#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer
{
    public static class UserManagerExtensions
    {
        public async static Task<TUser?> FindByIdOrEmail<TUser>(this UserManager<TUser> userManager, string? idOrEmail) where TUser : class
        {
            if (string.IsNullOrEmpty(idOrEmail))
                return null;
            if (idOrEmail.Contains('@'))
                return await userManager.FindByEmailAsync(idOrEmail);
            else
                return await userManager.FindByIdAsync(idOrEmail);
        }
    }
}
