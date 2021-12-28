using BTCPayServer.Data;
using BTCPayServer.Plugins.LNbank.Services.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

// https://dotnetstories.com/blog/How-to-implement-a-custom-base-class-for-razor-views-in-ASPNET-Core-en-7106773524?o=rss
namespace BTCPayServer.Plugins.LNbank.Pages
{
    public abstract class BasePageModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        protected readonly WalletService WalletService;
        protected string UserId => _userManager.GetUserId(User);
        
        protected BasePageModel(
            UserManager<ApplicationUser> userManager,
            WalletService walletService)
        {
            _userManager = userManager;
            WalletService = walletService;
        }
    }
}
