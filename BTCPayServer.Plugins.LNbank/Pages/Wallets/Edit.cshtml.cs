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
    public class EditModel : BasePageModel
    {
        public Wallet Wallet { get; set; }

        public EditModel(
            UserManager<ApplicationUser> userManager, 
            WalletService walletService) : base(userManager, walletService) {}

        public async Task<IActionResult> OnGetAsync(string walletId)
        {
            Wallet = await WalletService.GetWallet(new WalletQuery {
                UserId = UserId,
                WalletId = walletId,
                IncludeTransactions = true
            });

            if (Wallet == null) return NotFound();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string walletId)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            Wallet = await WalletService.GetWallet(new WalletQuery {
                UserId = UserId,
                WalletId = walletId,
                IncludeTransactions = true
            });

            if (Wallet == null) return NotFound();

            if (await TryUpdateModelAsync(Wallet, "wallet", w => w.Name))
            {
                await WalletService.AddOrUpdateWallet(Wallet);
                return RedirectToPage("./Index", new { walletId });
            }

            return Page();
        }
    }
}
