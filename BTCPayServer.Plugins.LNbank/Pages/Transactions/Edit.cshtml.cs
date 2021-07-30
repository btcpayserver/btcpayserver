using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.LNbank.Data.Models;
using BTCPayServer.Plugins.LNbank.Services.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.LNbank.Pages.Transactions
{
    public class EditModel : BasePageModel
    {
        public string WalletId { get; set; }
        public Transaction Transaction { get; set; }

        public EditModel(
            UserManager<ApplicationUser> userManager, 
            WalletService walletService) : base(userManager, walletService) {}

        public async Task<IActionResult> OnGetAsync(string walletId, string transactionId)
        {
            WalletId = walletId;
            Transaction = await WalletService.GetTransaction(new TransactionQuery
            {
                UserId = UserId,
                WalletId = walletId,
                TransactionId = transactionId
            });

            if (Transaction == null) return NotFound();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string walletId, string transactionId)
        {
            WalletId = walletId;
            Transaction = await WalletService.GetTransaction(new TransactionQuery
            {
                UserId = UserId,
                WalletId = walletId,
                TransactionId = transactionId
            });

            if (!ModelState.IsValid) return Page();
            if (Transaction == null) return NotFound();

            if (await TryUpdateModelAsync(Transaction, "transaction", t => t.Description))
            {
                await WalletService.UpdateTransaction(Transaction);
                return RedirectToPage("/Wallets/Index", new { walletId });
            }

            return Page();
        }
    }
}
