using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Server;

namespace BTCPayServer.Security.OpenId
{
    public class LogoutEventHandler : IOpenIddictServerHandler<OpenIddictServerEvents.HandleLogoutRequestContext>
    {
        protected readonly SignInManager<ApplicationUser> _signInManager;
        public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
    OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.HandleLogoutRequestContext>()
                .UseScopedHandler<LogoutEventHandler>()
                .Build();
        public LogoutEventHandler(SignInManager<ApplicationUser> signInManager)
        {
            _signInManager = signInManager;
        }

        public async ValueTask HandleAsync(
            OpenIddictServerEvents.HandleLogoutRequestContext notification)
        {
            await _signInManager.SignOutAsync();
        }
    }
}
