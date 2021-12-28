using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Plugins.LNbank.Data.Models;
using BTCPayServer.Plugins.LNbank.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.LNbank.Pages.Wallets
{
    
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class CreateModel : BasePageModel
    {
        public Wallet Wallet { get; set; }

        public CreateModel(
            UserManager<ApplicationUser> userManager, 
            WalletService walletService) : base(userManager, walletService) {}

        public IActionResult OnGet()
        {
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            Wallet = new Wallet
            {
                UserId = UserId
            };

            if (!await TryUpdateModelAsync(Wallet, "wallet", w => w.Name)) return Page();
            
            await WalletService.AddOrUpdateWallet(Wallet);
            return RedirectToPage("./Index", new { walletId = Wallet.WalletId });
        }
    }
}
