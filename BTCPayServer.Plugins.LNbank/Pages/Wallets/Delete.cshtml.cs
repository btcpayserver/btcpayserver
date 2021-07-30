using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.LNbank.Data.Models;
using BTCPayServer.Plugins.LNbank.Services.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.LNbank.Pages.Wallets
{
    public class DeleteModel : BasePageModel
    {
        public Wallet Wallet { get; set; }

        public DeleteModel(
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
            Wallet = await WalletService.GetWallet(new WalletQuery {
                UserId = UserId,
                WalletId = walletId
            });

            if (Wallet == null) return NotFound();

            await WalletService.RemoveWallet(Wallet);

            return RedirectToPage("./Index");
        }
    }
}
