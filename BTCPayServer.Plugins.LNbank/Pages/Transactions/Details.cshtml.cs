using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Plugins.LNbank.Data.Models;
using BTCPayServer.Plugins.LNbank.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.LNbank.Pages.Transactions
{
    
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class DetailsModel : BasePageModel
    {
        public string WalletId { get; set; }
        public Transaction Transaction { get; set; }

        public DetailsModel(
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
    }
}
